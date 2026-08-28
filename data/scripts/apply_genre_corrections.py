"""
Réinjecte dans tracks.json la relecture manuelle des genres faite par l'utilisateur dans
data/scripts/output/genre_retravaillé.txt (retour utilisateur du 28/08 : les suggestions
automatiques — Deezer, MusicBrainz, l'éditeur web — n'étaient pas satisfaisantes, il a préféré
encoder les genres lui-même à partir des listes "Titre - Auteur" générées plus tôt).

Format attendu, une ligne par morceau :
    Titre - Auteur | genre1, genre2 | années AAAA

Le préfixe "Titre - Auteur" doit correspondre EXACTEMENT à celui des listes titres_XX.txt
générées précédemment (mêmes title/artist que tracks.json) — c'est ce qui sert de clé de
correspondance, aucun fuzzy matching. Exception connue : "Bo le lavabo" était crédité à tort à
"Chansons Françaises" dans la liste générée ; l'utilisateur a signalé que le vrai artiste est
"Vincent Lagaf'" — ce script corrige aussi ce champ artist en plus des tags.

Contrairement à audit_genres_musicbrainz.py, il n'y a AUCUN garde-fou de confiance ici : cette
liste est une relecture humaine, donc REMPLACE entièrement le champ "tags" du morceau (décennie
+ genres), sans comparaison avec l'existant.

Usage :
    python apply_genre_corrections.py [--dry-run]
"""

from __future__ import annotations

import argparse
import json
import sys
from collections import defaultdict
from pathlib import Path

SCRIPTS_DIR = Path(__file__).parent
DATA_DIR = SCRIPTS_DIR.parent
TRACKS_JSON_PATH = DATA_DIR / "tracks.json"
INPUT_PATH = SCRIPTS_DIR / "output" / "genre_retravaillé.txt"

# Corrections d'artiste connues, à appliquer AVANT le rapprochement par "Titre - Auteur" (voir
# docstring du module) — la liste générée créditait "Bo le lavabo" à un artiste générique.
ARTIST_FIXES = {
    ("Bo le lavabo", "Chansons Françaises"): "Vincent Lagaf'",
}

DECADES_AVANT_1970 = {"1930", "1940", "1950", "1960"}

# Apostrophes typographiques diverses vues dans tracks.json (') vs la liste retravaillée au
# clavier standard (') — normalisées UNIQUEMENT pour le rapprochement, jamais réécrites dans
# tracks.json. Idem pour les espaces multiples (coquille pré-existante, ex. "L' air Du Vent").
def normaliser_cle(s: str) -> str:
    s = s.replace("’", "'").replace("‘", "'").replace("`", "'")
    s = " ".join(s.split())
    s = s.replace("' ", "'")  # coquille pré-existante type "L' air" vs "L'air"
    return s.casefold()  # tolère les écarts de casse ("N'Est" vs "N'est")


def decade_tag(annees_str: str) -> str:
    year = annees_str.replace("années", "").strip()
    if year in DECADES_AVANT_1970:
        return "avant-1970"
    return f"annees-{year}"


def parser_ligne(ligne: str) -> tuple[str, list[str], str] | None:
    if "|" not in ligne:
        return None
    parts = [p.strip() for p in ligne.split("|")]
    if len(parts) != 3:
        return None
    titre_auteur, genres_str, annees_str = parts
    genres = [g.strip().lower() for g in genres_str.split(",") if g.strip()]
    return titre_auteur, genres, annees_str


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dry-run", action="store_true", help="Affiche les changements sans réécrire tracks.json")
    args = parser.parse_args()

    if not INPUT_PATH.exists():
        sys.exit(f"{INPUT_PATH} introuvable.")
    if not TRACKS_JSON_PATH.exists():
        sys.exit(f"{TRACKS_JSON_PATH} introuvable.")

    tracks = json.loads(TRACKS_JSON_PATH.read_text(encoding="utf-8"))

    # Rapprochement par clé normalisée AVANT toute correction d'artiste (la liste retravaillée
    # a été générée avec l'ancien artist, voir ARTIST_FIXES) — une clé peut pointer vers
    # plusieurs morceaux (doublons title+artist réels dans le catalogue, ex. deux éditions du
    # même titre), consommés dans l'ordre où ils apparaissent dans tracks.json.
    cle_vers_tracks: dict[str, list[dict]] = defaultdict(list)
    for t in tracks:
        cle_vers_tracks[normaliser_cle(f"{t.get('title', '')} - {t.get('artist', '')}")].append(t)

    lignes_non_reconnues: list[str] = []
    ids_non_trouves: list[str] = []
    nb_modifies = 0
    nb_inchanges = 0
    ids_traites: set[str] = set()

    for numero, ligne_brute in enumerate(INPUT_PATH.read_text(encoding="utf-8").splitlines(), start=1):
        ligne = ligne_brute.strip()
        if not ligne:
            continue
        parsed = parser_ligne(ligne)
        if parsed is None:
            lignes_non_reconnues.append(f"L{numero}: {ligne_brute}")
            continue

        titre_auteur, genres, annees_str = parsed
        candidats = cle_vers_tracks.get(normaliser_cle(titre_auteur))
        if not candidats:
            ids_non_trouves.append(f"L{numero}: {titre_auteur}")
            continue
        track = candidats.pop(0)

        ids_traites.add(track["id"])
        nouveaux_tags = [decade_tag(annees_str), *genres]
        if nouveaux_tags != (track.get("tags") or []):
            print(f"  {track['id']} ({track.get('title','?')}) : {track.get('tags')} -> {nouveaux_tags}", file=sys.stderr)
            track["tags"] = nouveaux_tags
            nb_modifies += 1
        else:
            nb_inchanges += 1

    # Corrections d'artiste APRÈS le rapprochement (voir docstring d'ARTIST_FIXES).
    for track in tracks:
        fix = ARTIST_FIXES.get((track.get("title", ""), track.get("artist", "")))
        if fix:
            print(f"Correction artiste : « {track['title']} » : {track['artist']!r} -> {fix!r}", file=sys.stderr)
            track["artist"] = fix

    tous_ids = {t["id"] for t in tracks}
    ids_absents_du_fichier = tous_ids - ids_traites

    if lignes_non_reconnues:
        print(f"\nATTENTION : {len(lignes_non_reconnues)} ligne(s) mal formée(s), ignorée(s) :", file=sys.stderr)
        for l in lignes_non_reconnues:
            print(f"  {l}", file=sys.stderr)

    if ids_non_trouves:
        print(f"\nATTENTION : {len(ids_non_trouves)} ligne(s) sans morceau correspondant dans tracks.json :", file=sys.stderr)
        for l in ids_non_trouves:
            print(f"  {l}", file=sys.stderr)

    if ids_absents_du_fichier:
        print(f"\nATTENTION : {len(ids_absents_du_fichier)} morceau(x) de tracks.json absent(s) du fichier (tags inchangés) :", file=sys.stderr)
        for tid in ids_absents_du_fichier:
            t = next(t for t in tracks if t["id"] == tid)
            print(f"  {tid} : {t.get('title')} - {t.get('artist')}", file=sys.stderr)

    print(f"\n{nb_modifies} morceau(x) modifié(s), {nb_inchanges} inchangé(s).", file=sys.stderr)

    if args.dry_run:
        print("--dry-run : tracks.json non modifié.", file=sys.stderr)
        return

    TRACKS_JSON_PATH.write_text(json.dumps(tracks, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{TRACKS_JSON_PATH} mis à jour.", file=sys.stderr)


if __name__ == "__main__":
    main()
