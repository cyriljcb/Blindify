"""
Export de tracks.json vers un CSV pour relecture/correction des tags au tableur (retour
utilisateur du playtest 2026-08-24 : un morceau de Renaud classé "électro" par erreur — le
bucketing par mots-clés sur les genres Spotify reste faillible sur les cas rares, voir
docs/architecture.md section 3 point 3).

Colonnes : id, title, artist, year, genres (Spotify, lecture seule — juste pour juger), tags
(éditable), tauxReussite (V2, lecture seule — voir stats_common.py, aide à juger la difficulté
pendant la curation). genres/tags sont des listes jointes par "; " dans la cellule (CSV = une
valeur par cellule, pas de tableau natif).

Usage :
    python export_tags_csv.py [--out CHEMIN] [--tag TAG | --sans-tags]

    --tag TAG : n'exporte que les morceaux portant ce tag (ex. --tag electro), pour relire
    rapidement une seule catégorie plutôt que tout le catalogue.
    --sans-tags : n'exporte que les morceaux sans aucun tag — typiquement les morceaux tout
    juste ajoutés par sync_daily.py (tags jamais renseignés automatiquement, voir ce script).

Une fois corrigé au tableur, réinjecter avec import_tags_csv.py.
"""

from __future__ import annotations

import argparse
import csv
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

from stats_common import agreger, charger_stats_json

SCRIPTS_DIR = Path(__file__).parent
DATA_DIR = SCRIPTS_DIR.parent
TRACKS_JSON_PATH = DATA_DIR / "tracks.json"
STATS_JSON_PATH = DATA_DIR / "stats.json"
OUTPUT_DIR = SCRIPTS_DIR / "output"

CSV_FIELDS = ["id", "title", "artist", "year", "genres", "tags", "tauxReussite"]


def charger_tracks_json() -> list[dict]:
    if not TRACKS_JSON_PATH.exists():
        sys.exit(f"{TRACKS_JSON_PATH} introuvable.")
    return json.loads(TRACKS_JSON_PATH.read_text(encoding="utf-8"))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", default=None, help="Chemin du CSV en sortie (défaut : data/scripts/output/tags_AAAAMMJJ.csv)")
    parser.add_argument("--tag", default=None, help="N'exporte que les morceaux portant ce tag")
    parser.add_argument("--sans-tags", action="store_true", help="N'exporte que les morceaux sans aucun tag")
    args = parser.parse_args()

    if args.tag and args.sans_tags:
        sys.exit("--tag et --sans-tags sont exclusifs.")

    tracks = charger_tracks_json()
    if args.tag:
        tracks = [t for t in tracks if args.tag in (t.get("tags") or [])]
        if not tracks:
            sys.exit(f"Aucun morceau avec le tag « {args.tag} ».")
    elif args.sans_tags:
        tracks = [t for t in tracks if not t.get("tags")]
        if not tracks:
            sys.exit("Aucun morceau sans tag.")

    stats = charger_stats_json(STATS_JSON_PATH)

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    out_path = Path(args.out) if args.out else OUTPUT_DIR / f"tags_{datetime.now(timezone.utc):%Y%m%d}.csv"

    with out_path.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=CSV_FIELDS)
        writer.writeheader()
        for track in tracks:
            taux = agreger(stats.get(track["id"])).taux_reussite
            writer.writerow(
                {
                    "id": track["id"],
                    "title": track.get("title", ""),
                    "artist": track.get("artist", ""),
                    "year": track.get("year", ""),
                    "genres": "; ".join(track.get("genres") or []),
                    "tags": "; ".join(track.get("tags") or []),
                    "tauxReussite": "" if taux is None else round(taux, 3),
                }
            )

    print(f"{len(tracks)} morceau(x) exporté(s) dans {out_path}.", file=sys.stderr)
    print("Édite la colonne 'tags' au tableur (encodage utf-8-sig — s'ouvre correctement dans Excel), "
          "puis réinjecte avec import_tags_csv.py.", file=sys.stderr)


if __name__ == "__main__":
    main()
