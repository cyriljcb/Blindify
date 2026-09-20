"""
Rapport de difficulté mesurée (V2, section 12.2) — savoir quels morceaux sont trouvés facilement
et lesquels personne ne trouve jamais. Une information pour l'organisateur, jamais un paramètre du
jeu : ni `SelectionnerMorceaux`, ni le choix de la cible, ni la génération QCM ne lisent ce rapport
ou `stats.json` au-delà de `PlayCount` (voir docs/architecture.md section 4).

Lit `stats.json` + `tracks.json`, ne modifie rien. Sort un CSV (utf-8-sig, comme l'export des tags)
trié du plus dur au plus facile (les morceaux jamais mesurés — "insuffisant" — en fin de liste).

Colonnes : id, title, artist, year, tags, n, tauxReussite, tauxTitre, tauxAuteur, tauxAnnee,
tauxAbsence, tempsMoyenBonneReponseS, niveau.

niveau : facile (>= --seuil-facile), moyen, difficile (<= --seuil-difficile), ou insuffisant si
n < --min-n.

Usage :
    python report_difficulty.py [--out CHEMIN] [--tag TAG] [--sans-bonus]
                                 [--min-n 5] [--seuil-facile 0.70] [--seuil-difficile 0.30]
"""

from __future__ import annotations

import argparse
import csv
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

from stats_common import agreger, charger_stats_json, niveau_difficulte

SCRIPTS_DIR = Path(__file__).parent
DATA_DIR = SCRIPTS_DIR.parent
TRACKS_JSON_PATH = DATA_DIR / "tracks.json"
STATS_JSON_PATH = DATA_DIR / "stats.json"
OUTPUT_DIR = SCRIPTS_DIR / "output"

CSV_FIELDS = [
    "id", "title", "artist", "year", "tags", "n",
    "tauxReussite", "tauxTitre", "tauxAuteur", "tauxAnnee", "tauxAbsence",
    "tempsMoyenBonneReponseS", "niveau",
]


def charger_tracks_json() -> list[dict]:
    if not TRACKS_JSON_PATH.exists():
        sys.exit(f"{TRACKS_JSON_PATH} introuvable.")
    return json.loads(TRACKS_JSON_PATH.read_text(encoding="utf-8"))


def arrondi(valeur: float | None, chiffres: int) -> str:
    return "" if valeur is None else str(round(valeur, chiffres))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", default=None, help="Chemin du CSV en sortie (défaut : data/scripts/output/difficulte_AAAAMMJJ.csv)")
    parser.add_argument("--tag", default=None, help="N'inclut que les morceaux portant ce tag")
    parser.add_argument("--sans-bonus", action="store_true", help="Exclut les réponses données en question bonus (audio ralenti, biaisé)")
    parser.add_argument("--min-n", type=int, default=5, help="Nombre minimal de réponses pour ne pas être 'insuffisant' (défaut 5)")
    parser.add_argument("--seuil-facile", type=float, default=0.70, help="Taux de réussite à partir duquel un morceau est 'facile' (défaut 0.70)")
    parser.add_argument("--seuil-difficile", type=float, default=0.30, help="Taux de réussite en dessous duquel un morceau est 'difficile' (défaut 0.30)")
    args = parser.parse_args()

    tracks = charger_tracks_json()
    if args.tag:
        tracks = [t for t in tracks if args.tag in (t.get("tags") or [])]
        if not tracks:
            sys.exit(f"Aucun morceau avec le tag « {args.tag} ».")

    stats = charger_stats_json(STATS_JSON_PATH)

    lignes = []
    for track in tracks:
        agg = agreger(stats.get(track["id"]), exclure_bonus=args.sans_bonus)
        niveau = niveau_difficulte(agg, min_n=args.min_n, seuil_facile=args.seuil_facile, seuil_difficile=args.seuil_difficile)
        lignes.append(
            {
                "id": track["id"],
                "title": track.get("title", ""),
                "artist": track.get("artist", ""),
                "year": track.get("year", ""),
                "tags": "; ".join(track.get("tags") or []),
                "n": agg.n,
                "tauxReussite": arrondi(agg.taux_reussite, 3),
                "tauxTitre": arrondi(agg.taux_par_cible.get("Titre"), 3),
                "tauxAuteur": arrondi(agg.taux_par_cible.get("Auteur"), 3),
                "tauxAnnee": arrondi(agg.taux_par_cible.get("Annee"), 3),
                "tauxAbsence": arrondi(agg.taux_absence, 3),
                "tempsMoyenBonneReponseS": arrondi(agg.temps_moyen_bonne_reponse_s, 2),
                "niveau": niveau,
                # Champ interne pour le tri, retiré avant écriture (pas dans CSV_FIELDS).
                "_tri": (1, 0.0) if agg.taux_reussite is None else (0, agg.taux_reussite),
            }
        )

    lignes.sort(key=lambda r: r["_tri"])  # du plus dur (taux bas) au plus facile ; insuffisant en dernier
    for ligne in lignes:
        del ligne["_tri"]

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    out_path = Path(args.out) if args.out else OUTPUT_DIR / f"difficulte_{datetime.now(timezone.utc):%Y%m%d}.csv"

    with out_path.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=CSV_FIELDS)
        writer.writeheader()
        writer.writerows(lignes)

    print(f"{len(lignes)} morceau(x) exporté(s) dans {out_path}.", file=sys.stderr)


if __name__ == "__main__":
    main()
