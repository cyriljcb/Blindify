"""
Détecte automatiquement les paires de morceaux réellement confondues en jeu (V2, section 12.3) —
propose des candidats `trapWith` à partir des confusions mesurées plutôt qu'à la main. Le script
propose, l'organisateur valide au tableur (colonne "valider"), comme pour les tags (voir
export_tags_csv.py).

Lit `Confusions` dans stats.json (jamais modifié ici). Une paire (X, Y) est candidate si :
  - Presente >= --presente-min (défaut 3)
  - Choisi >= --choisi-min (défaut 2)
  - Choisi / Presente >= --taux-min (défaut 0.4)
Le volume de parties familiales reste faible — ces trois seuils sont des arguments, à ajuster à
l'usage plutôt que figés en dur.

Exclut les paires déjà présentes dans `trapWith`. Signale `symetrique=oui` quand la confusion
existe aussi dans l'autre sens (Y confondu avec X, au moins une fois).

Colonnes : id, title, artist, confonduId, confonduTitle, confonduArtist, presente, choisi, taux,
symetrique, valider (vide — mettre "x" pour valider une paire, réinjecter avec import_traps_csv.py).

Usage :
    python suggest_traps.py [--out CHEMIN] [--presente-min 3] [--choisi-min 2] [--taux-min 0.4]
"""

from __future__ import annotations

import argparse
import csv
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

from stats_common import charger_stats_json

SCRIPTS_DIR = Path(__file__).parent
DATA_DIR = SCRIPTS_DIR.parent
TRACKS_JSON_PATH = DATA_DIR / "tracks.json"
STATS_JSON_PATH = DATA_DIR / "stats.json"
OUTPUT_DIR = SCRIPTS_DIR / "output"

CSV_FIELDS = ["id", "title", "artist", "confonduId", "confonduTitle", "confonduArtist", "presente", "choisi", "taux", "symetrique", "valider"]


def charger_tracks_json() -> list[dict]:
    if not TRACKS_JSON_PATH.exists():
        sys.exit(f"{TRACKS_JSON_PATH} introuvable.")
    return json.loads(TRACKS_JSON_PATH.read_text(encoding="utf-8"))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", default=None, help="Chemin du CSV en sortie (défaut : data/scripts/output/pieges_AAAAMMJJ.csv)")
    parser.add_argument("--presente-min", type=int, default=3, help="Nombre minimal de fois où le distracteur a été présenté (défaut 3)")
    parser.add_argument("--choisi-min", type=int, default=2, help="Nombre minimal de fois où le distracteur a été choisi (défaut 2)")
    parser.add_argument("--taux-min", type=float, default=0.4, help="Ratio choisi/présenté minimal (défaut 0.4)")
    args = parser.parse_args()

    tracks = charger_tracks_json()
    tracks_par_id = {t["id"]: t for t in tracks}
    stats = charger_stats_json(STATS_JSON_PATH)

    lignes = []
    for track_id, entry in stats.items():
        track = tracks_par_id.get(track_id)
        if track is None:
            continue  # morceau retiré du catalogue depuis (tracks.json reste la source de vérité)

        confusions = entry.get("Confusions") or {}
        trap_with_actuel = set(track.get("trapWith") or [])

        for confondu_id, valeurs in confusions.items():
            confondu = tracks_par_id.get(confondu_id)
            if confondu is None:
                continue
            if confondu_id in trap_with_actuel:
                continue

            presente = valeurs.get("Presente", 0)
            choisi = valeurs.get("Choisi", 0)
            if presente < args.presente_min or choisi < args.choisi_min:
                continue
            taux = choisi / presente if presente else 0.0
            if taux < args.taux_min:
                continue

            confusion_inverse = (stats.get(confondu_id) or {}).get("Confusions", {}).get(track_id)
            symetrique = bool(confusion_inverse and confusion_inverse.get("Choisi", 0) >= 1)

            lignes.append(
                {
                    "id": track_id,
                    "title": track.get("title", ""),
                    "artist": track.get("artist", ""),
                    "confonduId": confondu_id,
                    "confonduTitle": confondu.get("title", ""),
                    "confonduArtist": confondu.get("artist", ""),
                    "presente": presente,
                    "choisi": choisi,
                    "taux": round(taux, 3),
                    "symetrique": "oui" if symetrique else "non",
                    "valider": "",
                }
            )

    if not lignes:
        print("Aucune paire candidate avec ces seuils.", file=sys.stderr)
        return

    lignes.sort(key=lambda r: r["taux"], reverse=True)

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    out_path = Path(args.out) if args.out else OUTPUT_DIR / f"pieges_{datetime.now(timezone.utc):%Y%m%d}.csv"

    with out_path.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=CSV_FIELDS)
        writer.writeheader()
        writer.writerows(lignes)

    print(f"{len(lignes)} paire(s) candidate(s) exportée(s) dans {out_path}.", file=sys.stderr)
    print("Marque 'x' dans la colonne 'valider' pour les paires à ajouter à trapWith, "
          "puis réinjecte avec import_traps_csv.py.", file=sys.stderr)


if __name__ == "__main__":
    main()
