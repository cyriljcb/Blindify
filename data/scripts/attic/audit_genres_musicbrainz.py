"""
PROTOTYPE — audit alternatif des tags "genre large" via MusicBrainz, en recoupant par ISRC
(code unique par enregistrement) plutôt que par nom d'artiste. Complète audit_genres.py
(genres Spotify de l'artiste, ambigu sur les homonymes) et audit_genres_deezer.py (source
Deezer) : ici la base est MusicBrainz, une base ouverte communautaire, et la clé de
recoupement est l'ISRC du morceau lui-même — pas le nom de l'artiste — donc pas de risque de
confusion entre deux artistes homonymes (ex. "Chorus" français vs groupe indien du même nom).

Codes ISRC récupérés via l'API Spotify (déjà présents dans la réponse standard GET /tracks,
champ external_ids.isrc) : pas d'appel Spotify supplémentaire dédié, juste le champ qu'on
n'exploitait pas encore.

Contrainte non négociable : l'API MusicBrainz limite à 1 requête/seconde et exige un
User-Agent descriptif (dépasser le seuil risque un blocage de l'IP). Sur ~1200 morceaux ça
prendrait ~20 minutes : ce prototype tourne donc sur un ÉCHANTILLON (--limit, défaut 150),
pas tout le catalogue. Échantillon priorisé : d'abord les morceaux déjà repérés comme
suspects par audit_genres.py (data/scripts/output/genres_20260827.csv — les plus
susceptibles d'être mal tagués), complété au besoin par d'autres morceaux tirés au hasard
jusqu'à --limit.

Point technique découvert en testant l'API réelle (voir docstring de suggerer_tags) :
l'endpoint ISRC de MusicBrainz n'accepte QUE inc=tags, pas inc=genres (contrairement à
l'endpoint /recording) — "genres is not a valid inc parameter for the isrc resource". On
utilise donc uniquement les tags libres (folksonomie, texte en minuscules façon
Last.fm : "electro house", "tech house", "pop rap"...), pas de champ "genre" structuré côté
MusicBrainz pour cette route.

Usage :
    python audit_genres_musicbrainz.py [--limit 150] [--delay-seconds 1.0] [--refresh] [--out FICHIER]

Résultats mis en cache (output/genre_musicbrainz_cache.json), --refresh pour tout re-vérifier.
Ne modifie JAMAIS tracks.json (comme audit_genres.py/import_tags_csv.py) et n'exécute aucune
commande git.
"""

from __future__ import annotations

import argparse
import csv
import json
import os
import random
import re
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

import requests
from dotenv import load_dotenv

from fetch_spotify_playlist import _api_get, get_access_token

SCRIPTS_DIR = Path(__file__).parent
DATA_DIR = SCRIPTS_DIR.parent
TRACKS_JSON_PATH = DATA_DIR / "tracks.json"
OUTPUT_DIR = SCRIPTS_DIR / "output"
CACHE_PATH = OUTPUT_DIR / "genre_musicbrainz_cache.json"
PRIORITY_CSV_PATH = OUTPUT_DIR / "genres_20260827.csv"

SPOTIFY_API_BASE = "https://api.spotify.com/v1"
SPOTIFY_BATCH_SIZE = 50

MUSICBRAINZ_API_BASE = "https://musicbrainz.org/ws/2"
MUSICBRAINZ_USER_AGENT = "Blindify-DataAudit/1.0 (projet hobby local)"
MUSICBRAINZ_DELAY_SECONDS = 1.0  # non négociable — voir docstring du module
MUSICBRAINZ_MAX_RETRIES = 5
MUSICBRAINZ_TIMEOUT = 30  # musicbrainz.org répond parfois avec >15-20s de latence, testé en direct

# Session réutilisée (keep-alive) pour tous les appels MusicBrainz : testé en direct, une
# connexion TCP/TLS neuve à chaque appel augmente nettement le taux de ReadTimeout.
_musicbrainz_session = requests.Session()
_musicbrainz_session.headers.update({"User-Agent": MUSICBRAINZ_USER_AGENT, "Accept": "application/json"})

CSV_FIELDS = [
    "id", "title", "artist", "tags_actuels", "isrc",
    "musicbrainz_tags_bruts", "suggestion_musicbrainz", "tags",
]

DECADE_TAGS = {"avant-1970", "annees-1970", "annees-1980", "annees-1990", "annees-2000", "annees-2010", "annees-2020"}
TAGS_AD_HOC = {"disney"}

# Mapping mots-clés -> même 9 buckets que audit_genres.py (docs/architecture.md section 3
# point 3), mais construit à partir de tags MusicBrainz réels inspectés à la main (folksonomie
# libre façon Last.fm, PAS les genres structurés Spotify/Deezer) :
#   - "Alors on danse" (Stromae) -> club, dance, electro house, electronic, hip hop, house,
#     party, pop, pop rap, progressive house, tech house
#   - "Smells Like Teen Spirit" (Nirvana), "Back In Black" (AC/DC) -> grunge/rock, hard rock
#   - "Sweet Dreams" (Marilyn Manson), KISS -> industrial metal, glam metal, hard rock
#   - "Despacito", "Waka Waka" -> latin pop, reggaeton, world
#   - "Temperature" (Sean Paul) -> dancehall, reggae
#   - "Lose Yourself" (Eminem) -> hip hop, rap
#   - "Ma philosophie" (Amel Bent) -> r&b, soul, french pop
# Comme pour audit_genres.py, "house"/"electronic" générique reste un signal fiable ici
# (contrairement à "french house" côté Spotify qui polluait la chanson française) car
# MusicBrainz ne mélange pas ce tag avec les artistes de variété.
#
# Correction (retour utilisateur du 28/08, CSV "bizarre") : "dance" a été retiré du bucket
# electro. C'est un mot beaucoup trop générique en folksonomie Last.fm — il apparaît comme
# simple descripteur d'ambiance sur quasiment n'importe quel genre dansant (disco, funk, r&b,
# pop...), pas seulement l'électro. Il faisait passer des morceaux clairement disco/soul
# (Boney M, Pointer Sisters, Earth Wind & Fire) en "electro" à cause d'un tag "dance-pop" ou
# "club" isolé, sans aucun autre signal électronique réel (pas de "electronic"/"synth"/"house").
BUCKETS: dict[str, list[str]] = {
    "metal": ["metal", "hardcore", "screamo", "grindcore"],
    "rock": ["rock", "punk", "grunge"],
    "pop": ["pop"],
    "electro": ["electro", "techno", "trance", "edm", "dubstep", "drum and bass", "dnb", "synth", "house"],
    "rap": ["rap", "hip hop", "hip-hop", "trap"],
    # "rhythm and blues" (forme longue vue sur Amel Bent côté MusicBrainz, en plus de "r&b").
    "rnb-funk-jazz": ["r&b", "rnb", "rhythm and blues", "funk", "jazz", "soul", "motown"],
    "variete-francaise": ["chanson", "variete", "variété", "french pop", "francoton", "francophone"],
    # "regueton" : variante orthographique vue telle quelle dans un tag MusicBrainz réel (Sean
    # Paul - Temperature), en plus de "reggaeton".
    "latino": ["latin", "reggaeton", "regueton", "salsa", "bachata", "cumbia", "flamenco"],
    # "dance hall" (avec espace) vu à côté de "dancehall" (sans espace) sur le même morceau.
    "monde": ["world", "afrobeat", "african", "reggae", "arab", "bollywood", "dancehall", "dance hall", "zouk", "kompa", "shatta"],
}


def charger_tracks_json() -> list[dict]:
    if not TRACKS_JSON_PATH.exists():
        sys.exit(f"{TRACKS_JSON_PATH} introuvable.")
    return json.loads(TRACKS_JSON_PATH.read_text(encoding="utf-8"))


def charger_cache() -> dict[str, dict]:
    if not CACHE_PATH.exists():
        return {}
    return json.loads(CACHE_PATH.read_text(encoding="utf-8"))


def sauvegarder_cache(cache: dict[str, dict]) -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    CACHE_PATH.write_text(json.dumps(cache, ensure_ascii=False, indent=2), encoding="utf-8")


def charger_ids_prioritaires() -> list[str]:
    """Ids du CSV d'audit_genres.py du 2026-08-27 : les morceaux déjà repérés comme suspects
    (tag actuel != suggestion Spotify fraîche), donc les plus susceptibles d'être mal tagués —
    priorité sur l'échantillon MusicBrainz forcément restreint (limite 1 req/s)."""
    if not PRIORITY_CSV_PATH.exists():
        print(f"Attention : {PRIORITY_CSV_PATH} introuvable, échantillon 100% aléatoire.", file=sys.stderr)
        return []
    with PRIORITY_CSV_PATH.open("r", encoding="utf-8-sig", newline="") as f:
        return [row["id"] for row in csv.DictReader(f)]


def construire_echantillon(tracks: list[dict], limit: int) -> list[dict]:
    tracks_par_id = {t["id"]: t for t in tracks}
    ids_prioritaires = [i for i in charger_ids_prioritaires() if i in tracks_par_id]

    echantillon: list[dict] = []
    vus: set[str] = set()
    for track_id in ids_prioritaires:
        if track_id not in vus:
            echantillon.append(tracks_par_id[track_id])
            vus.add(track_id)
        if len(echantillon) >= limit:
            return echantillon

    restants = [t for t in tracks if t["id"] not in vus]
    random.shuffle(restants)
    for track in restants:
        echantillon.append(track)
        if len(echantillon) >= limit:
            break

    return echantillon


def recuperer_isrc(tracks: list[dict], token: str, delay_seconds: float) -> dict[str, str]:
    """GET /tracks par lots de 50 -> external_ids.isrc, déjà dans la réponse standard Spotify
    (pas d'appel dédié)."""
    isrc_par_id: dict[str, str] = {}
    for i in range(0, len(tracks), SPOTIFY_BATCH_SIZE):
        batch = tracks[i : i + SPOTIFY_BATCH_SIZE]
        print(f"[Spotify ISRC {i + len(batch)}/{len(tracks)}]", file=sys.stderr)
        data = _api_get(f"{SPOTIFY_API_BASE}/tracks", token, params={"ids": ",".join(t["spotifyId"] for t in batch)})
        for track, item in zip(batch, data.get("tracks", [])):
            isrc = (item or {}).get("external_ids", {}).get("isrc")
            if isrc:
                isrc_par_id[track["id"]] = isrc
        time.sleep(delay_seconds)
    return isrc_par_id


def interroger_musicbrainz(isrc: str) -> list[str] | None:
    """GET /isrc/{isrc}?inc=tags — retourne les tags MusicBrainz agrégés sur tous les
    "recordings" liés à cet ISRC (un ISRC peut correspondre à plusieurs enregistrements/masters
    catalogués séparément), ou None si l'ISRC est inconnu de MusicBrainz (404).

    inc=genres N'EST PAS accepté par cette route (testé : "genres is not a valid inc parameter
    for the isrc resource", contrairement à /recording/{mbid}) — seuls les tags libres
    (folksonomie) sont disponibles ici.
    """
    url = f"{MUSICBRAINZ_API_BASE}/isrc/{isrc}"
    params = {"inc": "tags", "fmt": "json"}

    for tentative in range(MUSICBRAINZ_MAX_RETRIES):
        try:
            response = _musicbrainz_session.get(url, params=params, timeout=MUSICBRAINZ_TIMEOUT)
        except requests.exceptions.RequestException as e:
            print(f"  MusicBrainz timeout/erreur réseau ({isrc}, tentative {tentative + 1}) : {e}", file=sys.stderr)
            continue

        if response.status_code == 404:
            return None
        if response.status_code == 503:
            print(f"  MusicBrainz 503 (rate limit ?), attente 5s ({isrc})...", file=sys.stderr)
            time.sleep(5)
            continue
        try:
            response.raise_for_status()
            data = response.json()
        except (requests.exceptions.RequestException, ValueError) as e:
            # Vu en test : réponse 200 avec corps vide/invalide de façon sporadique — traité
            # comme un raté réseau à retenter, pas une absence d'ISRC (ne pas confondre avec 404).
            print(f"  MusicBrainz réponse invalide ({isrc}, tentative {tentative + 1}) : {e}", file=sys.stderr)
            continue

        recordings = data.get("recordings", [])
        tags = sorted({t["name"] for rec in recordings for t in rec.get("tags", [])})
        return tags

    print(f"  Échec MusicBrainz après {MUSICBRAINZ_MAX_RETRIES} tentatives, ISRC ignoré : {isrc}", file=sys.stderr)
    return None


# Nombre minimal de tags bruts distincts renvoyés par MusicBrainz pour qu'on fasse confiance à
# la suggestion au point d'ÉCRASER les tags-genre actuels. Correction (retour utilisateur du
# 28/08) : un ISRC avec un seul tag brut (souvent un tag poubelle comme "autre", un tag
# auto-référentiel type "jean-jacques goldman", ou un simple "chanson"/"pop" isolé) ne porte pas
# assez d'information pour justifier d'effacer des tags actuels corrects (ex. "Envole-moi" perdait
# pop+variete-francaise à cause du seul tag "jean-jacques goldman"). En dessous du seuil, on
# garde les tags actuels inchangés — même logique que l'absence totale de tags MusicBrainz.
SEUIL_TAGS_BRUTS_MINIMUM = 3


def suggerer_tags(tags_musicbrainz: list[str]) -> list[str]:
    tags_normalises = [t.casefold() for t in tags_musicbrainz]
    suggestions = []
    for bucket, mots_cles in BUCKETS.items():
        # (?!y) exclut les formes adjectivales du type "funky" (ambiance) qui ne doivent pas
        # déclencher le bucket "funk" (genre) — retour utilisateur du 28/08 : a-ha (synth-pop)
        # récoltait à tort "rnb-funk-jazz" à cause du seul tag d'ambiance "funky".
        motifs = [re.compile(re.escape(mot) + r"(?!y)") for mot in mots_cles]
        if any(motif.search(t) for t in tags_normalises for motif in motifs):
            suggestions.append(bucket)
    return suggestions


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--limit", type=int, default=150, help="Taille de l'échantillon (défaut 150 — voir docstring, 1200 morceaux prendrait ~20 min)")
    parser.add_argument("--delay-seconds", type=float, default=MUSICBRAINZ_DELAY_SECONDS, help="Pause entre chaque requête MusicBrainz (défaut 1.0s, NON NÉGOCIABLE — risque de blocage IP en dessous)")
    parser.add_argument("--refresh", action="store_true", help="Ignore le cache, re-vérifie tous les morceaux de l'échantillon")
    parser.add_argument("--out", default=None, help="Chemin du CSV en sortie (défaut : data/scripts/output/genres_musicbrainz_AAAAMMJJ.csv)")
    args = parser.parse_args()

    if args.delay_seconds < 1.0:
        sys.exit("--delay-seconds doit être >= 1.0 (limite MusicBrainz, voir docstring du module).")

    load_dotenv(SCRIPTS_DIR / ".env")
    client_id = os.environ.get("SPOTIFY_CLIENT_ID")
    client_secret = os.environ.get("SPOTIFY_CLIENT_SECRET")
    if not client_id or not client_secret:
        sys.exit(
            "SPOTIFY_CLIENT_ID / SPOTIFY_CLIENT_SECRET manquants - copie .env.example en .env "
            "et renseigne tes identifiants (https://developer.spotify.com/dashboard)."
        )

    tous_tracks = [t for t in charger_tracks_json() if t.get("spotifyId")]
    echantillon = construire_echantillon(tous_tracks, args.limit)
    print(f"Échantillon : {len(echantillon)} morceau(x) (sur {len(tous_tracks)} avec spotifyId).", file=sys.stderr)

    print("Authentification Spotify...", file=sys.stderr)
    token = get_access_token(client_id, client_secret)

    print("Récupération des ISRC via Spotify...", file=sys.stderr)
    isrc_par_id = recuperer_isrc(echantillon, token, delay_seconds=0.3)
    sans_isrc = [t for t in echantillon if t["id"] not in isrc_par_id]
    if sans_isrc:
        print(f"  {len(sans_isrc)} morceau(x) sans ISRC Spotify, ignoré(s).", file=sys.stderr)

    cache = {} if args.refresh else charger_cache()

    isrcs_uniques = sorted({isrc for isrc in isrc_par_id.values()})
    a_interroger = [isrc for isrc in isrcs_uniques if isrc not in cache]
    print(
        f"{len(a_interroger)} ISRC(s) unique(s) à interroger sur MusicBrainz "
        f"(sur {len(isrcs_uniques)}, cache inclus) — ~{len(a_interroger) * args.delay_seconds / 60:.1f} min à {args.delay_seconds}s/requête...",
        file=sys.stderr,
    )

    for i, isrc in enumerate(a_interroger, start=1):
        print(f"[MusicBrainz {i}/{len(a_interroger)}] {isrc}", file=sys.stderr)
        tags = interroger_musicbrainz(isrc)
        cache[isrc] = {"tags": tags}  # tags == None si ISRC inconnu de MusicBrainz (404)
        sauvegarder_cache(cache)
        time.sleep(args.delay_seconds)

    # Comparaison tags actuels (genre large uniquement, décennie/ad hoc préservés à l'identique
    # — même logique que audit_genres.py, pour rester réinjectable tel quel via
    # import_tags_csv.py) vs suggestion MusicBrainz.
    lignes = []
    nb_couverts = 0
    nb_ignores_signal_faible = 0
    for track in echantillon:
        isrc = isrc_par_id.get(track["id"])
        entree = cache.get(isrc) if isrc else None
        mb_tags = entree["tags"] if entree else None  # None = pas d'ISRC Spotify, 404, ou tags vides

        tags_actuels_complets = track.get("tags") or []
        tags_genre_actuels = [t for t in tags_actuels_complets if t not in DECADE_TAGS and t not in TAGS_AD_HOC]
        tags_hors_genre = [t for t in tags_actuels_complets if t in DECADE_TAGS or t in TAGS_AD_HOC]

        if mb_tags:
            nb_couverts += 1

        tags_genre_suggeres = suggerer_tags(mb_tags) if mb_tags else []
        suggestion_fiable = (
            bool(mb_tags) and len(mb_tags) >= SEUIL_TAGS_BRUTS_MINIMUM and bool(tags_genre_suggeres)
        )
        if mb_tags and not suggestion_fiable:
            nb_ignores_signal_faible += 1

        lignes.append(
            {
                "id": track["id"],
                "title": track.get("title", ""),
                "artist": track.get("artist", ""),
                "tags_actuels": "; ".join(tags_actuels_complets),
                "isrc": isrc or "",
                "musicbrainz_tags_bruts": "; ".join(mb_tags) if mb_tags else "",
                "suggestion_musicbrainz": "; ".join(tags_genre_suggeres),
                # Tags pré-remplis pour réimport direct — seulement si la suggestion est jugée
                # fiable (assez de tags bruts, voir SEUIL_TAGS_BRUTS_MINIMUM) ; sinon on garde les
                # tags actuels inchangés (pas question d'effacer un tag existant sur la foi d'un
                # signal MusicBrainz trop maigre ou inexploitable).
                "tags": "; ".join(tags_hors_genre + (tags_genre_suggeres if suggestion_fiable else tags_genre_actuels)),
            }
        )

    lignes_divergentes = [
        l for l in lignes
        if l["musicbrainz_tags_bruts"] and set(l["suggestion_musicbrainz"].split("; ")) != set(
            t for t in l["tags_actuels"].split("; ") if t and t not in DECADE_TAGS and t not in TAGS_AD_HOC
        )
    ]

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    out_path = Path(args.out) if args.out else OUTPUT_DIR / f"genres_musicbrainz_{datetime.now(timezone.utc):%Y%m%d}.csv"

    with out_path.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=CSV_FIELDS)
        writer.writeheader()
        writer.writerows(lignes)

    print(
        f"\nTerminé : {len(echantillon)} morceau(x) de l'échantillon, "
        f"{nb_couverts} avec des tags MusicBrainz exploitables ({nb_couverts * 100 // max(len(echantillon), 1)}% de couverture), "
        f"{len(lignes_divergentes)} écart(s) parmi les morceaux couverts, "
        f"{nb_ignores_signal_faible} suggestion(s) ignorée(s) faute de {SEUIL_TAGS_BRUTS_MINIMUM}+ tags bruts fiables "
        f"(tags actuels conservés tels quels). Export complet dans {out_path}.",
        file=sys.stderr,
    )
    print(
        "Relis la colonne 'tags' (pré-remplie avec la suggestion MusicBrainz quand disponible, "
        "sinon tags actuels inchangés) au tableur, corrige-la si besoin, puis réinjecte avec "
        "import_tags_csv.py (même colonne 'tags' que export_tags_csv.py).",
        file=sys.stderr,
    )


if __name__ == "__main__":
    main()
