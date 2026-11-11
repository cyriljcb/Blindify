"""Corrige les incohérences du catalogue relevées par l'audit du 2026-11-10.

Simulation par défaut : rien n'est écrit, un rapport détaillé est produit dans
output/corriger_catalogue_rapport.json et un résumé est affiché. `--apply` écrit tracks.json
(atomiquement, voir tracks_io.py) après une sauvegarde horodatée tracks.json.bak-<date>.

Corrections appliquées par --apply :
  1. artists : remplit le champ `artists` (liste des artistes crédités) de chaque morceau qui ne
     l'a pas, en protégeant les noms qui contiennent une virgule ("Earth, Wind & Fire") et en
     éclatant "A featuring B". Le champ `artist` (texte affiché) devient la jointure ", ".
  2. titres : retire les suffixes d'édition restés dans les titres (« - From "Film" », « (with X) »,
     « - Acoustic; Live at… », remasters…), avec la même règle que l'import (nettoyer_titre).
  3. doublons : même titre normalisé + même premier artiste. Le morceau gardé est celui qui a un
     refrain renseigné, puis des tags, puis le plus ancien ; il récupère l'union des tags, genres et
     trapWith, et l'année la plus ancienne (une réédition porte souvent une année plus récente).
     Les trapWith qui pointaient vers un doublon retiré sont redirigés vers le morceau gardé.
     Les fichiers audio/cover des doublons retirés ne sont pas supprimés (listés dans le rapport).

Signalés seulement (décision humaine nécessaire) :
  4. décennie taguée ≠ year (ex. Les lacs du Connemara 2004 tagué annees-1980) — à passer dans
     audit_reissue_years.py / apply_reissue_years.py : souvent une année de réédition.
  5. karaokés et reprises (artiste = label de karaoké) — retirés seulement avec --retirer-karaoke.

Usage :
  python corriger_catalogue.py                 # simulation
  python corriger_catalogue.py --apply
  python corriger_catalogue.py --apply --retirer-karaoke
"""

from __future__ import annotations

import argparse
import re
import shutil
import sys
from collections import defaultdict
from datetime import datetime
from pathlib import Path

from fetch_spotify_playlist import nettoyer_titre
from tracks_io import artistes, cle_doublon, decouper_artistes, ecrire_json_atomique, est_version_karaoke, lire_json

SCRIPTS_DIR = Path(__file__).resolve().parent
TRACKS_JSON_PATH = SCRIPTS_DIR.parent / "tracks.json"
OUTPUT_DIR = SCRIPTS_DIR / "output"


# Titre portant encore une mention de version (« - Live @ Giel! », « (live at …) », « - Original
# Radio Mix ») : à éviter comme morceau gardé quand la version studio est dans le même groupe.
_TITRE_VERSION = re.compile(r"\s[-–]\s|[\(\[]\s*live\b|\blive\s+(?:at|@|in)\b", re.IGNORECASE)


def _priorite_garde(track: dict) -> tuple:
    return (
        track.get("refrainStartMs") is None,
        bool(_TITRE_VERSION.search(track.get("title") or "")),
        len(track.get("tags") or []) == 0,
        track.get("addedAt") or "9999",
    )


def _union(*listes) -> list:
    resultat = []
    for liste in listes:
        for v in liste or []:
            if v not in resultat:
                resultat.append(v)
    return resultat


def corriger_artistes(tracks: list[dict], rapport: dict) -> int:
    remplis = 0
    for t in tracks:
        if t.get("artists"):
            continue
        remplis += 1
        liste = decouper_artistes(t.get("artist", ""))
        t["artists"] = liste
        texte = ", ".join(liste)
        if texte != t.get("artist"):
            rapport["artistes_reecrits"].append({"id": t["id"], "avant": t.get("artist"), "apres": texte})
            t["artist"] = texte
        if any("," in a for a in liste):
            rapport["artistes_avec_virgule"].append({"id": t["id"], "artists": liste})
    return remplis


def corriger_titres(tracks: list[dict], rapport: dict) -> None:
    for t in tracks:
        nettoye = nettoyer_titre(t.get("title", ""), artistes(t))
        if nettoye and nettoye != t["title"]:
            rapport["titres_nettoyes"].append({"id": t["id"], "avant": t["title"], "apres": nettoye})
            t["title"] = nettoye


def fusionner_doublons(tracks: list[dict], rapport: dict) -> list[dict]:
    groupes: dict[tuple, list[dict]] = defaultdict(list)
    for t in tracks:
        groupes[cle_doublon(t)].append(t)

    redirection: dict[str, str] = {}
    retires: set[str] = set()
    for cle, membres in groupes.items():
        if len(membres) < 2 or not cle[0]:
            continue
        membres = sorted(membres, key=_priorite_garde)
        garde, autres = membres[0], membres[1:]
        annees = [m.get("year") for m in membres if m.get("year")]
        rapport["doublons"].append({
            "garde": {"id": garde["id"], "titre": garde["title"], "artiste": garde["artist"], "annee": garde.get("year"), "album": garde.get("album")},
            "retires": [{"id": a["id"], "annee": a.get("year"), "album": a.get("album"), "filePath": a.get("filePath")} for a in autres],
            "anneesDivergentes": len(set(annees)) > 1,
        })
        garde["tags"] = _union(garde.get("tags"), *(a.get("tags") for a in autres))
        garde["genres"] = _union(garde.get("genres"), *(a.get("genres") for a in autres))
        garde["trapWith"] = _union(garde.get("trapWith"), *(a.get("trapWith") for a in autres))
        if annees:
            garde["year"] = min(annees)
        for a in autres:
            redirection[a["id"]] = garde["id"]
            retires.add(a["id"])

    restants = [t for t in tracks if t["id"] not in retires]
    for t in restants:
        if t.get("trapWith"):
            nouveaux = _union([redirection.get(i, i) for i in t["trapWith"]])
            t["trapWith"] = [i for i in nouveaux if i != t["id"]]
    return restants


def signaler_decennies(tracks: list[dict], rapport: dict) -> None:
    for t in tracks:
        annee = t.get("year")
        decennies = [tag for tag in t.get("tags") or [] if tag.startswith("annees-") and tag[7:].isdigit()]
        if not annee or not decennies:
            continue
        if not any(int(tag[7:]) <= annee < int(tag[7:]) + 10 for tag in decennies):
            rapport["decennies_incoherentes"].append({"id": t["id"], "titre": t["title"], "artiste": t["artist"], "year": annee, "tags": decennies})


def signaler_karaoke(tracks: list[dict], rapport: dict) -> set[str]:
    ids = set()
    for t in tracks:
        if est_version_karaoke(t.get("title", ""), t.get("artist", ""), t.get("album")):
            ids.add(t["id"])
            rapport["karaoke"].append({"id": t["id"], "titre": t["title"], "artiste": t["artist"], "album": t.get("album")})
    return ids


def main() -> None:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--apply", action="store_true", help="écrit tracks.json (sinon simulation)")
    parser.add_argument("--retirer-karaoke", action="store_true", help="avec --apply : retire aussi les karaokés détectés")
    parser.add_argument("--tracks", type=Path, default=TRACKS_JSON_PATH)
    args = parser.parse_args()

    tracks = lire_json(args.tracks)
    total_initial = len(tracks)
    rapport = {k: [] for k in ("artistes_reecrits", "artistes_avec_virgule", "titres_nettoyes", "doublons", "decennies_incoherentes", "karaoke")}

    corriger_titres(tracks, rapport)
    artists_remplis = corriger_artistes(tracks, rapport)
    tracks = fusionner_doublons(tracks, rapport)
    signaler_decennies(tracks, rapport)
    karaokes = signaler_karaoke(tracks, rapport)
    if args.retirer_karaoke:
        tracks = [t for t in tracks if t["id"] not in karaokes]
    sans_artists = sum(1 for t in tracks if not artistes(t))

    OUTPUT_DIR.mkdir(exist_ok=True)
    chemin_rapport = OUTPUT_DIR / "corriger_catalogue_rapport.json"
    ecrire_json_atomique(chemin_rapport, rapport)

    retires = sum(len(d["retires"]) for d in rapport["doublons"])
    print(f"Catalogue : {total_initial} morceaux -> {len(tracks)} ({'appliqué' if args.apply else 'simulation'})")
    print(f"  titres nettoyés              : {len(rapport['titres_nettoyes'])}")
    print(f"  champ artists rempli         : {artists_remplis} morceaux, {len(rapport['artistes_reecrits'])} textes d'artiste réécrits")
    print(f"  noms d'artiste à virgule     : {len(rapport['artistes_avec_virgule'])} morceaux protégés")
    print(f"  doublons                     : {len(rapport['doublons'])} groupes, {retires} morceaux fusionnés "
          f"({sum(1 for d in rapport['doublons'] if d['anneesDivergentes'])} avec années divergentes)")
    print(f"  décennie != year (à relire)  : {len(rapport['decennies_incoherentes'])}")
    print(f"  karaokés/reprises détectés   : {len(rapport['karaoke'])}{' (retirés)' if args.retirer_karaoke else ' (signalés seulement)'}")
    print(f"  morceaux sans artiste        : {sans_artists}")
    print(f"Rapport détaillé : {chemin_rapport}")

    if not args.apply:
        print("Simulation : tracks.json non modifié. Relance avec --apply pour écrire.", file=sys.stderr)
        return

    sauvegarde = args.tracks.with_name(f"{args.tracks.name}.bak-{datetime.now():%Y%m%d-%H%M%S}")
    shutil.copy2(args.tracks, sauvegarde)
    ecrire_json_atomique(args.tracks, tracks)
    print(f"tracks.json écrit ({len(tracks)} morceaux) — sauvegarde : {sauvegarde}", file=sys.stderr)


if __name__ == "__main__":
    main()
