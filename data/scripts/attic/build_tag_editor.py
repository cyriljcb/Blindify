"""
Génère un éditeur de tags autonome (page HTML locale, aucun serveur requis) à partir de
tracks.json — retour utilisateur du 28/08 : les suggestions automatiques (Deezer, MusicBrainz)
ne sont pas assez fiables pour être appliquées telles quelles (voir docstring de
audit_genres_musicbrainz.py), l'utilisateur préfère encoder les genres lui-même. Cet outil lui
donne juste une interface pratique pour ça : cases à cocher pour les 9 buckets connus, tag
libre, indice MusicBrainz affiché à titre de suggestion cliquable (jamais pré-appliqué), export
CSV réinjectable via import_tags_csv.py.

Les modifications sont sauvegardées dans le localStorage du navigateur au fur et à mesure (clé
"blindify-tag-editor-v1") — rien n'est écrit dans tracks.json tant que le CSV exporté n'est pas
repassé dans import_tags_csv.py.

Comme tracks.json change entre deux sessions de relecture, relancer ce script avant de reprendre
le travail régénère la page avec les données à jour (le localStorage garde les modifications déjà
faites, indexées par id de morceau, donc rien n'est perdu en régénérant).

Usage :
    python build_tag_editor.py [--out FICHIER]
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
OUTPUT_DIR = SCRIPTS_DIR / "output"
TEMPLATE_PATH = SCRIPTS_DIR / "tag_editor_template.html"
MUSICBRAINZ_CSV_PATH = OUTPUT_DIR / "genres_musicbrainz_20260828.csv"

DECADE_TAGS = {"avant-1970", "annees-1970", "annees-1980", "annees-1990", "annees-2000", "annees-2010", "annees-2020"}

# Mêmes 9 buckets que audit_genres.py / audit_genres_musicbrainz.py (docs/architecture.md
# section 3 point 3) — ordre fixe pour un affichage stable des cases à cocher.
BUCKETS = ["metal", "rock", "pop", "electro", "rap", "rnb-funk-jazz", "variete-francaise", "latino", "monde"]


def charger_tracks_json() -> list[dict]:
    if not TRACKS_JSON_PATH.exists():
        sys.exit(f"{TRACKS_JSON_PATH} introuvable.")
    return json.loads(TRACKS_JSON_PATH.read_text(encoding="utf-8"))


def charger_hints_musicbrainz() -> dict[str, dict]:
    """Indices déjà collectés par audit_genres_musicbrainz.py (échantillon de 150, voir sa
    docstring) — affichés à titre de suggestion cliquable, jamais pré-appliqués."""
    if not MUSICBRAINZ_CSV_PATH.exists():
        print(f"Attention : {MUSICBRAINZ_CSV_PATH} introuvable, pas d'indice MusicBrainz affiché.", file=sys.stderr)
        return {}
    hints = {}
    with MUSICBRAINZ_CSV_PATH.open("r", encoding="utf-8-sig", newline="") as f:
        for row in csv.DictReader(f):
            raw = [t for t in row["musicbrainz_tags_bruts"].split("; ") if t]
            suggestion = [t for t in row["suggestion_musicbrainz"].split("; ") if t]
            if raw or suggestion:
                hints[row["id"]] = {"raw": raw, "suggestion": suggestion}
    return hints


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", default=None, help="Chemin du HTML en sortie (défaut : data/scripts/output/tag_editor.html)")
    args = parser.parse_args()

    tracks_json = charger_tracks_json()
    hints = charger_hints_musicbrainz()

    tracks = []
    for t in tracks_json:
        tags = t.get("tags") or []
        decade = next((tag for tag in tags if tag in DECADE_TAGS), None)
        tracks.append({
            "id": t["id"],
            "title": t.get("title", ""),
            "artist": t.get("artist", ""),
            "year": t.get("year", ""),
            "genres": t.get("genres") or [],
            "decade": decade,
            "baseTags": tags,
        })
    tracks.sort(key=lambda t: (t["artist"].casefold(), t["title"].casefold()))

    if not TEMPLATE_PATH.exists():
        sys.exit(f"{TEMPLATE_PATH} introuvable.")
    html = TEMPLATE_PATH.read_text(encoding="utf-8")

    def embed(value) -> str:
        return json.dumps(value, ensure_ascii=False).replace("</", "<\\/")

    html = html.replace("__DATA_JSON__", embed(tracks))
    html = html.replace("__HINTS_JSON__", embed(hints))
    html = html.replace("__BUCKETS_JSON__", embed(BUCKETS))
    html = html.replace("__TOTAL__", str(len(tracks)))

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    out_path = Path(args.out) if args.out else OUTPUT_DIR / "tag_editor.html"
    out_path.write_text(html, encoding="utf-8")

    print(
        f"{len(tracks)} morceau(x), {len(hints)} avec indice MusicBrainz. "
        f"Éditeur généré dans {out_path} — ouvre-le directement dans ton navigateur.",
        file=sys.stderr,
    )


if __name__ == "__main__":
    main()
