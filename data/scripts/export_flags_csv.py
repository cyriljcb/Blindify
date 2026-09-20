"""
Export de data/flags.json vers un CSV pour traiter les signalements en direct (V2, section 12.4)
au tableur, comme pour les tags (voir export_tags_csv.py) — le backend écrit flags.json au fil des
parties (GameHub.SignalerMorceau), ce script sert à faire le tri après coup.

Colonnes : flagId, date, trackId, title, artist, youtubeUrl, raison, commentaire, serieTags,
resolution (lecture seule — vide tant qu'aucune entrée n'existe dans flags_resolutions.json pour ce
flagId).

Usage :
    python export_flags_csv.py [--out CHEMIN] [--ouverts] [--raison RAISON] [--ids-only]

    --ouverts : n'exporte que les signalements pas encore résolus (absents de flags_resolutions.json).
    --raison RAISON : filtre sur une seule raison (ex. --raison MauvaiseVersion).
    --ids-only : sort uniquement une colonne "id" (trackId, dédoublonné) — pour passer directement
    la liste à redownload_tracks.py --csv.
"""

from __future__ import annotations

import argparse
import csv
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

SCRIPTS_DIR = Path(__file__).parent
DATA_DIR = SCRIPTS_DIR.parent
TRACKS_JSON_PATH = DATA_DIR / "tracks.json"
FLAGS_JSON_PATH = DATA_DIR / "flags.json"
FLAGS_RESOLUTIONS_JSON_PATH = DATA_DIR / "flags_resolutions.json"
OUTPUT_DIR = SCRIPTS_DIR / "output"

CSV_FIELDS = ["flagId", "date", "trackId", "title", "artist", "youtubeUrl", "raison", "commentaire", "serieTags", "resolution"]


def charger_tracks_json() -> list[dict]:
    if not TRACKS_JSON_PATH.exists():
        sys.exit(f"{TRACKS_JSON_PATH} introuvable.")
    return json.loads(TRACKS_JSON_PATH.read_text(encoding="utf-8"))


def charger_json_ou_defaut(path: Path, defaut):
    if not path.exists():
        return defaut
    return json.loads(path.read_text(encoding="utf-8"))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", default=None, help="Chemin du CSV en sortie (défaut : data/scripts/output/flags_AAAAMMJJ.csv)")
    parser.add_argument("--ouverts", action="store_true", help="N'exporte que les signalements pas encore résolus")
    parser.add_argument("--raison", default=None, help="Filtre sur une seule raison (ex. MauvaiseVersion)")
    parser.add_argument("--ids-only", action="store_true", help="Sort uniquement une colonne 'id' (trackId dédoublonné)")
    args = parser.parse_args()

    flags = charger_json_ou_defaut(FLAGS_JSON_PATH, [])
    if not flags:
        sys.exit("Aucun signalement dans flags.json.")

    resolutions = charger_json_ou_defaut(FLAGS_RESOLUTIONS_JSON_PATH, {})
    tracks_par_id = {t["id"]: t for t in charger_tracks_json()}

    if args.raison:
        flags = [f for f in flags if f.get("Raison") == args.raison]
    if args.ouverts:
        flags = [f for f in flags if f.get("Id") not in resolutions]
    if not flags:
        sys.exit("Aucun signalement ne correspond à ces filtres.")

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    if args.ids_only:
        ids = list(dict.fromkeys(f["TrackId"] for f in flags))  # dédoublonné, ordre stable
        out_path = Path(args.out) if args.out else OUTPUT_DIR / f"flags_ids_{datetime.now(timezone.utc):%Y%m%d}.csv"
        with out_path.open("w", encoding="utf-8-sig", newline="") as f:
            writer = csv.DictWriter(f, fieldnames=["id"])
            writer.writeheader()
            writer.writerows({"id": i} for i in ids)
        print(f"{len(ids)} id(s) exporté(s) dans {out_path}.", file=sys.stderr)
        return

    out_path = Path(args.out) if args.out else OUTPUT_DIR / f"flags_{datetime.now(timezone.utc):%Y%m%d}.csv"
    with out_path.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=CSV_FIELDS)
        writer.writeheader()
        for flag in flags:
            track = tracks_par_id.get(flag["TrackId"])
            resolution = resolutions.get(flag["Id"], {}).get("resolution", "")
            writer.writerow(
                {
                    "flagId": flag["Id"],
                    "date": flag.get("Horodatage", "").split("T")[0],
                    "trackId": flag["TrackId"],
                    "title": track.get("title", "") if track else "",
                    "artist": track.get("artist", "") if track else "",
                    "youtubeUrl": f"https://www.youtube.com/watch?v={track['youtubeId']}" if track and track.get("youtubeId") else "",
                    "raison": flag.get("Raison", ""),
                    "commentaire": flag.get("Commentaire") or "",
                    "serieTags": "; ".join(flag.get("SerieTags") or []),
                    "resolution": resolution,
                }
            )

    print(f"{len(flags)} signalement(s) exporté(s) dans {out_path}.", file=sys.stderr)
    print("Remplis la colonne 'resolution' (corrige/ignore/retire) au tableur, "
          "puis réinjecte avec import_flag_resolutions.py.", file=sys.stderr)


if __name__ == "__main__":
    main()
