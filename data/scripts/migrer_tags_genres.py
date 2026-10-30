"""
Migre les tags de genre large déjà posés vers la liste revue le 2026-10-25 (docs/architecture.md
section 11) :
    - "metal" est fondu dans "rock" (automatique) ;
    - "rnb-funk-jazz" est découpé en "funk-disco" (disco, funk, hi-NRG) ou "rnb-soul" (R&B,
      soul, Motown, jazz, blues), d'après le champ genres quand il tranche ;
    - "eurodance" est ajouté aux morceaux déjà tagués dont le champ genres contient "eurodance"
      (genre posé par artiste côté Spotify, donc toujours à vérifier). "pop" en est retiré
      (jamais un second genre), "electro" est gardé en second.

N'écrit JAMAIS dans tracks.json : sort un CSV (colonne "tags" proposée, "tagsActuels" pour
comparer), trié avec les lignes à vérifier en premier. Après correction au tableur :
    python import_tags_csv.py output/migration_tags_genres.csv

Les morceaux encore sans tags ne sont pas concernés : suggest_tags_llm.py (prompt v5) les
classera directement avec la nouvelle liste.

Usage :
    python migrer_tags_genres.py [--out CHEMIN]
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

GENRES_FUNK_DISCO = {"disco", "funk", "hi-nrg", "post-disco", "italo disco", "boogie", "jazz funk",
                     "disco house", "funk rock", "nu disco"}
GENRES_RNB_SOUL = {"r&b", "french r&b", "soul", "neo soul", "motown", "classic soul", "new jack swing",
                   "gospel", "smooth jazz", "soul jazz", "acid jazz", "jazz", "blues", "pop urbaine"}

CSV_FIELDS = ["id", "title", "artist", "album", "year", "genres", "tags", "tagsActuels", "aVerifier", "raison"]


def charger_tracks() -> list[dict]:
    if not TRACKS_JSON_PATH.exists():
        sys.exit(f"{TRACKS_JSON_PATH} introuvable.")
    return json.loads(TRACKS_JSON_PATH.read_text(encoding="utf-8"))


def remplacer(tags: list[str], ancien: str, nouveau: str) -> list[str]:
    """Remplace en gardant la position, sans créer de doublon."""
    resultat = []
    for t in tags:
        t = nouveau if t == ancien else t
        if t not in resultat:
            resultat.append(t)
    return resultat


def decouper_rnb_funk_jazz(track: dict) -> tuple[str, bool, str]:
    """Retourne (nouveau tag, à vérifier, raison). Sans indice dans genres, la décennie sert de
    repli : avant 1985, c'est le plus souvent du disco/funk, après du R&B."""
    genres = set(track.get("genres") or [])
    funk, soul = genres & GENRES_FUNK_DISCO, genres & GENRES_RNB_SOUL
    if funk and not soul:
        return "funk-disco", False, f"genres : {', '.join(sorted(funk))}"
    if soul and not funk:
        return "rnb-soul", False, f"genres : {', '.join(sorted(soul))}"
    if funk and soul:
        return "funk-disco", True, f"genres ambigus : {', '.join(sorted(funk | soul))}"
    year = track.get("year") or 0
    if 0 < year < 1985:
        return "funk-disco", True, f"aucun indice dans genres, repli sur l'année {year}"
    return "rnb-soul", True, f"aucun indice dans genres, repli sur l'année {year or '?'}"


def migrer(track: dict) -> tuple[list[str], bool, list[str]] | None:
    actuels = track.get("tags") or []
    if not actuels:
        return None
    tags, a_verifier, raisons = list(actuels), False, []

    if "metal" in tags:
        tags = remplacer(tags, "metal", "rock")
        raisons.append("metal -> rock")

    if "rnb-funk-jazz" in tags:
        nouveau, douteux, raison = decouper_rnb_funk_jazz(track)
        tags = remplacer(tags, "rnb-funk-jazz", nouveau)
        a_verifier |= douteux
        raisons.append(f"rnb-funk-jazz -> {nouveau} ({raison})")

    if "eurodance" in (track.get("genres") or []) and "eurodance" not in tags:
        tags = [t for t in tags if t != "pop"] + ["eurodance"]
        a_verifier = True
        raisons.append("eurodance d'après genres (posé par artiste, à confirmer)")

    if tags == actuels:
        return None
    return tags, a_verifier, raisons


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", type=Path, default=OUTPUT_DIR / "migration_tags_genres.csv")
    args = parser.parse_args()

    lignes = []
    for track in charger_tracks():
        resultat = migrer(track)
        if resultat is None:
            continue
        tags, a_verifier, raisons = resultat
        lignes.append({
            "id": track["id"],
            "title": track.get("title", ""),
            "artist": track.get("artist", ""),
            "album": track.get("album", ""),
            "year": track.get("year") or "",
            "genres": "; ".join(track.get("genres") or []),
            "tags": "; ".join(tags),
            "tagsActuels": "; ".join(track.get("tags") or []),
            "aVerifier": "oui" if a_verifier else "",
            "raison": " | ".join(raisons),
        })

    lignes.sort(key=lambda l: (l["aVerifier"] != "oui", l["raison"]))
    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=CSV_FIELDS)
        writer.writeheader()
        writer.writerows(lignes)

    nb_verif = sum(1 for l in lignes if l["aVerifier"] == "oui")
    print(f"{len(lignes)} morceau(x) à migrer, dont {nb_verif} à vérifier -> {args.out}", file=sys.stderr)


if __name__ == "__main__":
    main()
