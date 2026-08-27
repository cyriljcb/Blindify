"""
Audit du catalogue existant à la recherche de versions live/acoustiques/remix/cover
téléchargées par erreur (retour utilisateur 2026-08-25 — voir live_keywords.py pour le
pourquoi : la sélection dans download_audio.py se faisait uniquement par proximité de
durée, un titre suspect ne bloquait rien).

Pour chaque morceau de tracks.json, récupère le vrai titre de sa vidéo YouTube
(youtubeId) sans la télécharger, et exporte en CSV ceux dont le titre contient un mot-clé
suspect absent du titre officiel du morceau — à relire manuellement, comme le CSV des
tags (export_tags_csv.py). Ne modifie jamais tracks.json.

Usage :
    python audit_live_versions.py [--limit N] [--delay-seconds 1.5] [--refresh]

Les résultats YouTube déjà récupérés sont mis en cache (output/live_audit_cache.json) et
réutilisés d'un run à l'autre — ~1200 morceaux en un seul passage prendrait trop de temps
pour risquer de tout reperdre sur une interruption. --refresh ignore le cache et
re-vérifie tout.
"""

from __future__ import annotations

import argparse
import csv
import json
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

import yt_dlp

from live_keywords import mots_suspects

SCRIPTS_DIR = Path(__file__).parent
DATA_DIR = SCRIPTS_DIR.parent
TRACKS_JSON_PATH = DATA_DIR / "tracks.json"
OUTPUT_DIR = SCRIPTS_DIR / "output"
CACHE_PATH = OUTPUT_DIR / "live_audit_cache.json"

CSV_FIELDS = ["id", "title", "artist", "mots_cles_suspects", "youtube_title", "youtube_url"]


def charger_tracks_json() -> list[dict]:
    if not TRACKS_JSON_PATH.exists():
        sys.exit(f"{TRACKS_JSON_PATH} introuvable.")
    return json.loads(TRACKS_JSON_PATH.read_text(encoding="utf-8"))


def charger_cache() -> dict[str, dict]:
    if not CACHE_PATH.exists():
        return {}
    return json.loads(CACHE_PATH.read_text(encoding="utf-8"))


def sauvegarder_cache(cache: dict[str, dict]) -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    CACHE_PATH.write_text(json.dumps(cache, ensure_ascii=False, indent=2), encoding="utf-8")


def recuperer_titre_youtube(youtube_id: str) -> str:
    options = {"quiet": True, "no_warnings": True, "skip_download": True}
    with yt_dlp.YoutubeDL(options) as ydl:
        info = ydl.extract_info(f"https://www.youtube.com/watch?v={youtube_id}", download=False)
        return info.get("title", "")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--limit", type=int, default=None, help="Ne traiter que les N premiers morceaux (test)")
    parser.add_argument("--delay-seconds", type=float, default=1.5, help="Pause entre chaque requête YouTube (défaut 1.5s)")
    parser.add_argument("--refresh", action="store_true", help="Ignore le cache, re-vérifie tous les morceaux")
    parser.add_argument("--out", default=None, help="Chemin du CSV en sortie (défaut : data/scripts/output/live_audit_AAAAMMJJ.csv)")
    args = parser.parse_args()

    tracks = [t for t in charger_tracks_json() if t.get("youtubeId")]
    if args.limit:
        tracks = tracks[: args.limit]

    cache = {} if args.refresh else charger_cache()

    a_verifier = 0
    echecs = 0
    for i, track in enumerate(tracks, start=1):
        youtube_id = track["youtubeId"]
        if youtube_id in cache:
            continue

        print(f"[{i}/{len(tracks)}] {track.get('artist')} - {track.get('title')}", file=sys.stderr)
        try:
            titre_video = recuperer_titre_youtube(youtube_id)
            cache[youtube_id] = {"title": titre_video, "error": None}
        except Exception as e:  # noqa: BLE001 — un échec ne doit pas arrêter le lot
            print(f"  échec : {e}", file=sys.stderr)
            cache[youtube_id] = {"title": None, "error": str(e)}
            echecs += 1
        finally:
            sauvegarder_cache(cache)
            time.sleep(args.delay_seconds)

    lignes_suspectes = []
    for track in tracks:
        entree = cache.get(track["youtubeId"])
        if not entree or not entree.get("title"):
            continue
        mots = mots_suspects(entree["title"], track.get("title", ""))
        if mots:
            a_verifier += 1
            lignes_suspectes.append(
                {
                    "id": track["id"],
                    "title": track.get("title", ""),
                    "artist": track.get("artist", ""),
                    "mots_cles_suspects": "; ".join(mots),
                    "youtube_title": entree["title"],
                    "youtube_url": f"https://www.youtube.com/watch?v={track['youtubeId']}",
                }
            )

    echecs = sum(1 for v in cache.values() if v.get("error"))

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    out_path = Path(args.out) if args.out else OUTPUT_DIR / f"live_audit_{datetime.now(timezone.utc):%Y%m%d}.csv"

    with out_path.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=CSV_FIELDS)
        writer.writeheader()
        writer.writerows(lignes_suspectes)

    print(
        f"\nTerminé : {len(tracks)} morceau(x) vérifié(s) (cache inclus), "
        f"{a_verifier} suspect(s) exporté(s) dans {out_path}, {echecs} échec(s) de récupération.",
        file=sys.stderr,
    )
    print(
        "Relis la colonne 'mots_cles_suspects' au tableur : un faux positif (ex. reprise "
        "légitime, mot présent par coïncidence) se règle en ignorant la ligne. Pour un vrai "
        "cas, remplace le fichier audio correspondant (le script ne le fait pas lui-même).",
        file=sys.stderr,
    )


if __name__ == "__main__":
    main()
