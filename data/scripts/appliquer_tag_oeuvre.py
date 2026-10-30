"""
Complète fetch_spotify_search.py pour les morceaux de la liste déjà présents dans tracks.json
(section "dejaPresents" du rapport) : leur ajoute le tag œuvre et le champ `oeuvre`, en gardant
leurs autres tags — ils deviennent des morceaux "doubles" (docs/architecture.md section 6 :
question Film dans un thème Films & séries, question classique dans leurs autres thèmes).

Un morceau sans autre tag devient un morceau "film-serie" exclusif, comme ceux importés.

Usage :
    python appliquer_tag_oeuvre.py output/film_serie_rapport.json --tag film-serie [--dry-run]
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

TRACKS_JSON_PATH = Path(__file__).parent.parent / "tracks.json"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("rapport", help="Rapport JSON produit par fetch_spotify_search.py")
    parser.add_argument("--tag", required=True, help="Tag œuvre à ajouter (ex. film-serie)")
    parser.add_argument("--dry-run", action="store_true", help="Affiche les modifications sans écrire tracks.json")
    args = parser.parse_args()

    rapport = json.loads(Path(args.rapport).read_text(encoding="utf-8"))
    tracks = json.loads(TRACKS_JSON_PATH.read_text(encoding="utf-8"))
    par_id = {t["id"]: t for t in tracks}

    nb_modifies = 0
    for ligne in rapport.get("dejaPresents", []):
        track = par_id.get(ligne["idCatalogue"])
        if track is None:
            print(f"  introuvable : {ligne['idCatalogue']} ({ligne['trouve']})", file=sys.stderr)
            continue

        tags = track.get("tags") or []
        oeuvre = ligne["oeuvre"].strip()
        if args.tag in tags and track.get("oeuvre") == oeuvre:
            continue

        track["tags"] = tags + [args.tag] if args.tag not in tags else tags
        track["oeuvre"] = oeuvre
        nb_modifies += 1
        print(f"  {track['artist']} - {track['title']} -> {track['tags']}, oeuvre « {oeuvre} »", file=sys.stderr)

    print(f"\n{nb_modifies} morceau(x) modifié(s).", file=sys.stderr)
    if args.dry_run:
        print("--dry-run : tracks.json non modifié.", file=sys.stderr)
        return
    TRACKS_JSON_PATH.write_text(json.dumps(tracks, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
