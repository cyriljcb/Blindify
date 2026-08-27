"""
Réinjecte dans tracks.json les années corrigées par audit_reissue_years.py — deuxième moitié de
cet outil, même principe que import_tags_csv.py pour les tags.

Pour chaque ligne du CSV, remplace `year` par `year_probable` et met à jour le tag décennie en
conséquence (`avant-1970` / `annees-1970`…`annees-2020`, dérivé de `year_probable` — même
convention que les tags déjà présents dans tracks.json). Ne touche à aucun autre champ (tags
genre, titre, artiste...).

Usage :
    python apply_reissue_years.py CHEMIN.csv [--dry-run] [--exclude ID1,ID2,...]

    --exclude : ignore certains ids du CSV (ex. faux positif repéré à la relecture — une "Slowed
    Down Version" avec une date placeholder Spotify 1900, voir le run du 2026-08-27).
"""

from __future__ import annotations

import argparse
import csv
import json
import sys
from pathlib import Path

SCRIPTS_DIR = Path(__file__).parent
DATA_DIR = SCRIPTS_DIR.parent
TRACKS_JSON_PATH = DATA_DIR / "tracks.json"

DECADES = [1970, 1980, 1990, 2000, 2010, 2020]


def charger_tracks_json() -> list[dict]:
    if not TRACKS_JSON_PATH.exists():
        sys.exit(f"{TRACKS_JSON_PATH} introuvable.")
    return json.loads(TRACKS_JSON_PATH.read_text(encoding="utf-8"))


def sauvegarder_tracks_json(tracks: list[dict]) -> None:
    TRACKS_JSON_PATH.write_text(json.dumps(tracks, ensure_ascii=False, indent=2), encoding="utf-8")


def tag_decennie(year: int) -> str:
    if year < 1970:
        return "avant-1970"
    decennie = max(d for d in DECADES if d <= year)
    return f"annees-{decennie}"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("csv_path", help="CSV produit par audit_reissue_years.py")
    parser.add_argument("--dry-run", action="store_true", help="Affiche les changements sans réécrire tracks.json")
    parser.add_argument("--exclude", default="", help="Ids séparés par des virgules à ignorer (faux positifs)")
    args = parser.parse_args()

    csv_path = Path(args.csv_path)
    if not csv_path.exists():
        sys.exit(f"{csv_path} introuvable.")

    exclus = {v.strip() for v in args.exclude.split(",") if v.strip()}

    tracks = charger_tracks_json()
    tracks_par_id = {t["id"]: t for t in tracks}

    nb_modifies = 0
    ids_inconnus: list[str] = []
    ids_exclus: list[str] = []

    with csv_path.open("r", encoding="utf-8-sig", newline="") as f:
        for row in csv.DictReader(f):
            track_id = row["id"].strip()

            if track_id in exclus:
                ids_exclus.append(f"{track_id} ({row.get('title', '?')})")
                continue

            track = tracks_par_id.get(track_id)
            if track is None:
                ids_inconnus.append(track_id)
                continue

            nouvelle_annee = int(row["year_probable"])
            ancienne_annee = track.get("year")
            nouveau_tag = tag_decennie(nouvelle_annee)
            ancien_tag = tag_decennie(ancienne_annee) if ancienne_annee else None

            print(f"  {track_id} ({track.get('title', '?')}) : year {ancienne_annee} -> {nouvelle_annee}"
                  + (f", tag {ancien_tag} -> {nouveau_tag}" if ancien_tag != nouveau_tag else ""))

            track["year"] = nouvelle_annee
            tags = track.get("tags") or []
            if ancien_tag in tags:
                tags = [t for t in tags if t != ancien_tag]
            if nouveau_tag not in tags:
                tags = [nouveau_tag] + tags
            track["tags"] = tags
            nb_modifies += 1

    if ids_inconnus:
        print(f"\nATTENTION : {len(ids_inconnus)} id(s) du CSV introuvable(s) dans tracks.json, ignoré(s) : "
              f"{', '.join(ids_inconnus[:10])}{'...' if len(ids_inconnus) > 10 else ''}", file=sys.stderr)
    if ids_exclus:
        print(f"\n{len(ids_exclus)} id(s) exclu(s) via --exclude : {', '.join(ids_exclus)}", file=sys.stderr)

    print(f"\n{nb_modifies} morceau(x) modifié(s).", file=sys.stderr)

    if args.dry_run:
        print("--dry-run : tracks.json non modifié.", file=sys.stderr)
        return

    if nb_modifies == 0:
        print("Rien à écrire.", file=sys.stderr)
        return

    sauvegarder_tracks_json(tracks)
    print(f"{TRACKS_JSON_PATH} mis à jour.", file=sys.stderr)


if __name__ == "__main__":
    main()
