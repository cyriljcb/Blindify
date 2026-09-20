"""
Réinjecte dans tracks.json les tags corrigés au tableur (voir export_tags_csv.py) — deuxième
moitié du "Outil de curation des tags" recommandé dans docs/architecture.md section 11.

Ne touche QUE le champ "tags" de chaque morceau, jamais title/artist/year/genres/tauxReussite
(colonnes de lecture seule dans le CSV, présentes uniquement pour juger au tableur — tauxReussite,
V2, vient de stats_common.py) : les modifier dans le CSV n'a aucun effet ici. Un id du CSV absent de tracks.json est signalé et ignoré plutôt que de
faire échouer tout l'import. Un morceau de tracks.json absent du CSV garde ses tags actuels
inchangés — un export filtré par --tag (voir export_tags_csv.py) peut donc être réimporté tel
quel sans toucher au reste du catalogue.

Usage :
    python import_tags_csv.py CHEMIN.csv [--dry-run]
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


def charger_tracks_json() -> list[dict]:
    if not TRACKS_JSON_PATH.exists():
        sys.exit(f"{TRACKS_JSON_PATH} introuvable.")
    return json.loads(TRACKS_JSON_PATH.read_text(encoding="utf-8"))


def sauvegarder_tracks_json(tracks: list[dict]) -> None:
    TRACKS_JSON_PATH.write_text(json.dumps(tracks, ensure_ascii=False, indent=2), encoding="utf-8")


def parser_liste(cellule: str) -> list[str]:
    return [v.strip() for v in cellule.split(";") if v.strip()]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("csv_path", help="CSV corrigé (voir export_tags_csv.py)")
    parser.add_argument("--dry-run", action="store_true", help="Affiche les changements sans réécrire tracks.json")
    args = parser.parse_args()

    csv_path = Path(args.csv_path)
    if not csv_path.exists():
        sys.exit(f"{csv_path} introuvable.")

    tracks = charger_tracks_json()
    tracks_par_id = {t["id"]: t for t in tracks}

    # Vivier de tags déjà connus AVANT import — sert seulement à repérer une faute de frappe
    # probable après coup (ex. "electr0"), jamais à bloquer l'import (les tags ad hoc comme
    # "disney" sont volontairement libres, voir architecture.md section 3 point 3).
    tags_deja_connus = {tag for t in tracks for tag in (t.get("tags") or [])}

    nb_modifies = 0
    nb_inchanges = 0
    ids_inconnus: list[str] = []
    tags_nouveaux: set[str] = set()

    with csv_path.open("r", encoding="utf-8-sig", newline="") as f:
        for row in csv.DictReader(f):
            track_id = row["id"].strip()
            track = tracks_par_id.get(track_id)
            if track is None:
                ids_inconnus.append(track_id)
                continue

            nouveaux_tags = parser_liste(row["tags"])
            if nouveaux_tags != (track.get("tags") or []):
                print(f"  {track_id} ({track.get('title', '?')}) : {track.get('tags')} -> {nouveaux_tags}")
                track["tags"] = nouveaux_tags
                nb_modifies += 1
            else:
                nb_inchanges += 1

            tags_nouveaux |= set(nouveaux_tags) - tags_deja_connus

    if ids_inconnus:
        print(f"\nATTENTION : {len(ids_inconnus)} id(s) du CSV introuvable(s) dans tracks.json, ignoré(s) : "
              f"{', '.join(ids_inconnus[:10])}{'...' if len(ids_inconnus) > 10 else ''}", file=sys.stderr)
    if tags_nouveaux:
        print(f"\nNouveaux tags jamais vus dans le catalogue (vérifie qu'il n'y a pas de faute de frappe) : "
              f"{', '.join(sorted(tags_nouveaux))}", file=sys.stderr)

    print(f"\n{nb_modifies} morceau(x) modifié(s), {nb_inchanges} inchangé(s).", file=sys.stderr)

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
