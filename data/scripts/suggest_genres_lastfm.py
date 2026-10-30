"""
Suggère des valeurs pour le champ genres (taxonomie fine, 220 valeurs) à partir de
Last.fm track.getTopTags, pour les morceaux où ce champ est vide.

Pourquoi Last.fm et pas Spotify/Deezer/MusicBrainz (déjà essayés, voir attic/) :
lookup par couple artiste+titre au niveau piste (pas d'ambiguïté d'homonymie
artiste comme sur les genres Spotify par artiste), vocabulaire déjà proche du
nôtre (tags libres minuscules), gratuit, sans quota facturé.

N'écrit JAMAIS dans tracks.json — sort un CSV de suggestions à relire/valider
(réinjection manuelle via import_tags_csv.py ou équivalent). La décision
"encodage largement manuel" (voir apply_genre_corrections.py) reste intacte :
ce script propose, ne décide pas.

Deux filtres de qualité, tous deux dérivés du catalogue existant plutôt que
d'une liste écrite à la main :
  1. Whitelist : ne retenir un tag Last.fm que s'il fait déjà partie des valeurs
     "viables" du catalogue (>= --min-count occurrences dans genres, déf. 10) —
     en dessous, un genre ne peut générer aucun distracteur QCM ni servir de
     filtre de série (QcmGenerator.SeuilPartFrequenceGenreGenerique ne fixe
     qu'un seuil HAUT ; un genre à 1 occurrence "passe" le code mais reste
     inerte, faute d'un second morceau pour servir de leurre).
  2. Compatibilité avec les tags larges déjà présents sur 100% des morceaux
     (annees-*, pop, rock, eurodance, electro, rap, funk-disco, rnb-soul,
     variete-francaise, disney, monde, latino) : un tag suggéré n'est accepté que s'il a
     déjà co-occurré au moins --min-cooc fois avec l'un des tags larges (hors
     décennie) du morceau, ailleurs dans le catalogue. Empêche par ex.
     "synthpop" sur un morceau tagué seulement "rap".

Usage :
    python suggest_genres_lastfm.py [--min-count 10] [--min-cooc 2]
                                     [--limit N] [--out CHEMIN]

Nécessite LASTFM_API_KEY dans .env (voir .env.example, clé gratuite sur
last.fm/api/account/create). Throttlé à ~4 req/s (limite Last.fm ~5 req/s) —
pas de retry agressif en cas d'erreur réseau, le morceau est juste ignoré.
"""

from __future__ import annotations

import argparse
import collections
import csv
import json
import os
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

import requests
from dotenv import load_dotenv

SCRIPTS_DIR = Path(__file__).parent
DATA_DIR = SCRIPTS_DIR.parent
TRACKS_JSON_PATH = DATA_DIR / "tracks.json"
OUTPUT_DIR = SCRIPTS_DIR / "output"

API_URL = "https://ws.audioscrobbler.com/2.0/"
REQUEST_DELAY_S = 0.25

# Non informatifs pour juger la compatibilité d'un genre musical avec les tags
# larges existants (une décennie n'exclut aucun genre) — exclus du calcul de
# co-occurrence et de la comparaison de compatibilité.
TAGS_DECENNIE = {
    "annees-1970", "annees-1980", "annees-1990", "annees-2000",
    "annees-2010", "annees-2020", "avant-1970",
}


def charger_tracks() -> list[dict]:
    if not TRACKS_JSON_PATH.exists():
        sys.exit(f"{TRACKS_JSON_PATH} introuvable.")
    return json.loads(TRACKS_JSON_PATH.read_text(encoding="utf-8"))


def construire_whitelist(tracks: list[dict], min_count: int) -> set[str]:
    compteur = collections.Counter()
    for t in tracks:
        for g in t.get("genres") or []:
            compteur[g] += 1
    return {g for g, c in compteur.items() if c >= min_count}


def construire_cooccurrences(tracks: list[dict], whitelist: set[str]) -> dict[str, collections.Counter]:
    cooc: dict[str, collections.Counter] = collections.defaultdict(collections.Counter)
    for t in tracks:
        tags_larges = set(t.get("tags") or []) - TAGS_DECENNIE
        for g in t.get("genres") or []:
            if g in whitelist:
                for tag in tags_larges:
                    cooc[g][tag] += 1
    return cooc


def genre_compatible(genre: str, tags_piste: list[str], cooc: dict[str, collections.Counter], min_cooc: int) -> bool:
    tags_larges = set(tags_piste) - TAGS_DECENNIE
    compteur_genre = cooc.get(genre)
    if not compteur_genre:
        return False
    return any(compteur_genre.get(tag, 0) >= min_cooc for tag in tags_larges)


def lastfm_top_tags(api_key: str, artist: str, title: str) -> list[tuple[str, int]]:
    """[(tag, count)] triés par count décroissant, [] si rien trouvé ou erreur réseau."""
    try:
        resp = requests.get(
            API_URL,
            params={
                "method": "track.getTopTags",
                "artist": artist,
                "track": title,
                "autocorrect": 1,
                "api_key": api_key,
                "format": "json",
            },
            timeout=10,
        )
        resp.raise_for_status()
        data = resp.json()
    except (requests.RequestException, ValueError):
        return []

    toptags = (data.get("toptags") or {}).get("tag")
    if not toptags:
        return []
    if isinstance(toptags, dict):  # un seul tag -> Last.fm renvoie un objet, pas une liste
        toptags = [toptags]

    resultat = []
    for tag in toptags:
        try:
            resultat.append((str(tag["name"]).lower().strip(), int(tag["count"])))
        except (KeyError, TypeError, ValueError):
            continue
    resultat.sort(key=lambda x: -x[1])
    return resultat


def normaliser_pour_whitelist(tag: str, whitelist: set[str]) -> str | None:
    """Last.fm et notre taxonomie ne s'accordent pas toujours sur tiret vs espace
    (ex. "hip-hop" vs "hip hop"), alors que les deux formes existent légitimement
    ailleurs dans les 220 valeurs (ex. "hi-nrg", "post-disco") — donc pas de règle
    uniforme possible, on essaie juste les deux variantes contre la whitelist."""
    if tag in whitelist:
        return tag
    variante = tag.replace("-", " ") if "-" in tag else tag.replace(" ", "-")
    return variante if variante in whitelist else None


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--min-count", type=int, default=10, help="Seuil de viabilité pour la whitelist des genres (def. 10)")
    parser.add_argument("--min-cooc", type=int, default=2, help="Nb minimum de co-occurrences genre/tag pour juger compatible (def. 2)")
    parser.add_argument("--limit", type=int, default=None, help="Limite le nb de morceaux traités (pour tester)")
    parser.add_argument("--out", default=None, help="Chemin du CSV de suggestions en sortie")
    args = parser.parse_args()

    load_dotenv(SCRIPTS_DIR / ".env")
    api_key = os.environ.get("LASTFM_API_KEY")
    if not api_key:
        sys.exit("LASTFM_API_KEY manquant — voir .env.example.")

    tracks = charger_tracks()
    whitelist = construire_whitelist(tracks, args.min_count)
    cooc = construire_cooccurrences(tracks, whitelist)

    sans_genre = [t for t in tracks if not t.get("genres")]
    if args.limit:
        sans_genre = sans_genre[: args.limit]

    print(f"{len(sans_genre)} morceau(x) sans genre à traiter, whitelist de {len(whitelist)} valeurs.", file=sys.stderr)

    lignes = []
    candidats_hors_vocabulaire: collections.Counter = collections.Counter()

    for i, t in enumerate(sans_genre, 1):
        top_tags = lastfm_top_tags(api_key, t["artist"], t["title"])
        # Last.fm peut renvoyer deux variantes distinctes ("hip hop" et "hip-hop") qui
        # normalisent vers le même terme whitelisté — on garde le meilleur score des deux.
        meilleur_par_genre: dict[str, int] = {}
        for tag, count in top_tags:
            tag_normalise = normaliser_pour_whitelist(tag, whitelist)
            if tag_normalise:
                meilleur_par_genre[tag_normalise] = max(meilleur_par_genre.get(tag_normalise, 0), count)
            else:
                candidats_hors_vocabulaire[tag] += 1
        candidats = sorted(meilleur_par_genre.items(), key=lambda x: -x[1])
        acceptes = [
            (tag, count) for tag, count in candidats
            if genre_compatible(tag, t.get("tags") or [], cooc, args.min_cooc)
        ]
        acceptes_noms = {tag for tag, _ in acceptes}
        rejetes = [tag for tag, _ in candidats if tag not in acceptes_noms]

        if acceptes:
            lignes.append({
                "id": t["id"],
                "artist": t["artist"],
                "title": t["title"],
                "tags_existants": "; ".join(t.get("tags") or []),
                "suggestions": "; ".join(f"{tag} ({count})" for tag, count in acceptes),
                "rejetes_incompatibles_tags": "; ".join(rejetes),
            })

        if i % 50 == 0:
            print(f"  {i}/{len(sans_genre)}...", file=sys.stderr)
        time.sleep(REQUEST_DELAY_S)

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    out_path = Path(args.out) if args.out else OUTPUT_DIR / f"genres_suggested_{datetime.now(timezone.utc):%Y%m%d}.csv"
    fieldnames = ["id", "artist", "title", "tags_existants", "suggestions", "rejetes_incompatibles_tags"]
    with out_path.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(lignes)

    print(f"{len(lignes)}/{len(sans_genre)} morceau(x) avec au moins une suggestion retenue -> {out_path}", file=sys.stderr)

    if candidats_hors_vocabulaire:
        candidats_path = OUTPUT_DIR / f"genres_hors_vocabulaire_{datetime.now(timezone.utc):%Y%m%d}.csv"
        with candidats_path.open("w", encoding="utf-8-sig", newline="") as f:
            writer = csv.writer(f)
            writer.writerow(["tag_lastfm", "nb_morceaux"])
            for tag, count in candidats_hors_vocabulaire.most_common():
                writer.writerow([tag, count])
        print(
            f"{len(candidats_hors_vocabulaire)} tag(s) Last.fm hors vocabulaire (candidats d'extension) -> {candidats_path}",
            file=sys.stderr,
        )


if __name__ == "__main__":
    main()
