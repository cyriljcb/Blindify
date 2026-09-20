"""
Réinjecte dans tracks.json les paires de pièges validées au tableur (voir suggest_traps.py) —
deuxième moitié du pipeline "Pièges détectés automatiquement" (V2, section 12.3), calqué sur la
structure d'import_tags_csv.py.

Pour chaque ligne marquée "x" dans la colonne "valider" : ajoute confonduId au trapWith de id, et
l'inverse si symetrique=oui. N'enlève jamais rien (idempotent — relancer le même CSV plusieurs
fois ne duplique rien non plus, trapWith reste une liste sans doublon). Écriture atomique de
tracks.json (fichier temporaire adjacent puis renommage, comme AtomicJsonFile côté backend).

Usage :
    python import_traps_csv.py CHEMIN.csv [--dry-run]
"""

from __future__ import annotations

import argparse
import csv
import json
import os
import sys
from pathlib import Path

SCRIPTS_DIR = Path(__file__).parent
DATA_DIR = SCRIPTS_DIR.parent
TRACKS_JSON_PATH = DATA_DIR / "tracks.json"


def charger_tracks_json() -> list[dict]:
    if not TRACKS_JSON_PATH.exists():
        sys.exit(f"{TRACKS_JSON_PATH} introuvable.")
    return json.loads(TRACKS_JSON_PATH.read_text(encoding="utf-8"))


def sauvegarder_tracks_json_atomique(tracks: list[dict]) -> None:
    tmp_path = TRACKS_JSON_PATH.with_suffix(TRACKS_JSON_PATH.suffix + ".tmp")
    tmp_path.write_text(json.dumps(tracks, ensure_ascii=False, indent=2), encoding="utf-8")
    os.replace(tmp_path, TRACKS_JSON_PATH)  # atomique côté OS (même volume)


def ajouter_piege(track: dict, autre_id: str) -> bool:
    """Retourne True si trapWith a effectivement changé (pour le compteur de modifications)."""
    trap_with = track.setdefault("trapWith", [])
    if autre_id in trap_with:
        return False
    trap_with.append(autre_id)
    return True


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("csv_path", help="CSV validé (voir suggest_traps.py)")
    parser.add_argument("--dry-run", action="store_true", help="Affiche les changements sans réécrire tracks.json")
    args = parser.parse_args()

    csv_path = Path(args.csv_path)
    if not csv_path.exists():
        sys.exit(f"{csv_path} introuvable.")

    tracks = charger_tracks_json()
    tracks_par_id = {t["id"]: t for t in tracks}

    nb_modifies = 0
    nb_ignores = 0
    ids_inconnus: set[str] = set()

    with csv_path.open("r", encoding="utf-8-sig", newline="") as f:
        for row in csv.DictReader(f):
            if row.get("valider", "").strip().lower() != "x":
                continue

            track_id = row["id"].strip()
            confondu_id = row["confonduId"].strip()
            track = tracks_par_id.get(track_id)
            confondu = tracks_par_id.get(confondu_id)

            if track is None:
                ids_inconnus.add(track_id)
                continue
            if confondu is None:
                ids_inconnus.add(confondu_id)
                continue

            changement = ajouter_piege(track, confondu_id)
            if row.get("symetrique", "").strip().lower() == "oui":
                changement = ajouter_piege(confondu, track_id) or changement

            if changement:
                print(f"  {track_id} ({track.get('title', '?')}) <-> {confondu_id} ({confondu.get('title', '?')})")
                nb_modifies += 1
            else:
                nb_ignores += 1

    if ids_inconnus:
        print(f"\nATTENTION : id(s) du CSV introuvable(s) dans tracks.json, ignoré(s) : "
              f"{', '.join(sorted(ids_inconnus))}", file=sys.stderr)

    print(f"\n{nb_modifies} paire(s) ajoutée(s), {nb_ignores} déjà présente(s) ou sans effet.", file=sys.stderr)

    if args.dry_run:
        print("--dry-run : tracks.json non modifié.", file=sys.stderr)
        return

    if nb_modifies == 0:
        print("Rien à écrire.", file=sys.stderr)
        return

    sauvegarder_tracks_json_atomique(tracks)
    print(f"{TRACKS_JSON_PATH} mis à jour.", file=sys.stderr)


if __name__ == "__main__":
    main()
