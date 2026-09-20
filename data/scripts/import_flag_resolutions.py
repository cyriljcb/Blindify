"""
Réinjecte dans data/flags_resolutions.json les résolutions décidées au tableur (voir
export_flags_csv.py) — deuxième moitié du pipeline "Signalement en direct" (V2, section 12.4).

N'écrit QUE flags_resolutions.json — jamais tracks.json ni flags.json (voir CLAUDE.md : le backend
est seul à écrire flags.json, ce script est seul à écrire flags_resolutions.json). Une ligne dont la
colonne "resolution" est vide est ignorée (signalement encore ouvert). Un flagId déjà résolu peut
être ré-importé pour changer sa résolution ; un flagId absent du CSV garde sa résolution actuelle
inchangée.

Usage :
    python import_flag_resolutions.py CHEMIN.csv [--dry-run]
"""

from __future__ import annotations

import argparse
import csv
import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path

SCRIPTS_DIR = Path(__file__).parent
DATA_DIR = SCRIPTS_DIR.parent
FLAGS_RESOLUTIONS_JSON_PATH = DATA_DIR / "flags_resolutions.json"

RESOLUTIONS_VALIDES = {"corrige", "ignore", "retire"}


def charger_resolutions() -> dict[str, dict]:
    if not FLAGS_RESOLUTIONS_JSON_PATH.exists():
        return {}
    return json.loads(FLAGS_RESOLUTIONS_JSON_PATH.read_text(encoding="utf-8"))


def sauvegarder_resolutions_atomique(resolutions: dict[str, dict]) -> None:
    tmp_path = FLAGS_RESOLUTIONS_JSON_PATH.with_suffix(FLAGS_RESOLUTIONS_JSON_PATH.suffix + ".tmp")
    tmp_path.write_text(json.dumps(resolutions, ensure_ascii=False, indent=2), encoding="utf-8")
    os.replace(tmp_path, FLAGS_RESOLUTIONS_JSON_PATH)  # atomique côté OS (même volume)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("csv_path", help="CSV rempli (voir export_flags_csv.py)")
    parser.add_argument("--dry-run", action="store_true", help="Affiche les changements sans réécrire flags_resolutions.json")
    args = parser.parse_args()

    csv_path = Path(args.csv_path)
    if not csv_path.exists():
        sys.exit(f"{csv_path} introuvable.")

    resolutions = charger_resolutions()
    aujourdhui = datetime.now(timezone.utc).strftime("%Y-%m-%d")

    nb_modifies = 0
    nb_ignores = 0
    valeurs_invalides: set[str] = set()

    with csv_path.open("r", encoding="utf-8-sig", newline="") as f:
        for row in csv.DictReader(f):
            resolution = row.get("resolution", "").strip().lower()
            if not resolution:
                continue  # signalement encore ouvert, rien à reporter

            flag_id = row["flagId"].strip()
            if resolution not in RESOLUTIONS_VALIDES:
                valeurs_invalides.add(resolution)
                continue

            existant = resolutions.get(flag_id)
            if existant is not None and existant.get("resolution") == resolution:
                nb_ignores += 1
                continue

            print(f"  {flag_id} ({row.get('title', '?')}) -> {resolution}")
            resolutions[flag_id] = {"resolution": resolution, "date": aujourdhui}
            nb_modifies += 1

    if valeurs_invalides:
        print(f"\nATTENTION : valeur(s) de 'resolution' non reconnue(s), ignorée(s) — attendu {sorted(RESOLUTIONS_VALIDES)} : "
              f"{', '.join(sorted(valeurs_invalides))}", file=sys.stderr)

    print(f"\n{nb_modifies} résolution(s) ajoutée(s)/modifiée(s), {nb_ignores} déjà à jour.", file=sys.stderr)

    if args.dry_run:
        print("--dry-run : flags_resolutions.json non modifié.", file=sys.stderr)
        return

    if nb_modifies == 0:
        print("Rien à écrire.", file=sys.stderr)
        return

    sauvegarder_resolutions_atomique(resolutions)
    print(f"{FLAGS_RESOLUTIONS_JSON_PATH} mis à jour.", file=sys.stderr)


if __name__ == "__main__":
    main()
