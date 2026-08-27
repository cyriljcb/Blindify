"""
Orchestrateur du pipeline de préparation des données (docs/architecture.md section 3), pensé
pour tourner chaque matin via cron sur le Raspberry Pi (voir crontab.example dans ce dossier).

Lit playlists.json (liste de {"id": ..., "tags": [...]}) — un ID de playlist Spotify par
entrée, à remplir soi-même. Pour l'ensemble des playlists suivies :
  1. récupère les morceaux + genres via l'API Spotify (réutilise fetch_spotify_playlist.py) ;
  2. fusionne tout dans un seul export, dédoublonné par id Spotify (un même morceau peut
     apparaître dans plusieurs playlists suivies — ses tags sont alors fusionnés) ;
  3. lance download_audio.py sur cet export fusionné — ce script est déjà idempotent (il saute
     les morceaux déjà présents dans tracks.json ou dont le fichier audio existe déjà), donc
     relancer ce script chaque matin ne re-télécharge que les morceaux réellement nouveaux.

Ne touche jamais aux tags/trapWith personnalisés déjà en place dans tracks.json (uniquement les
morceaux nouvellement ajoutés) — le travail de curation manuelle au tableur (architecture.md
section 11, "Outil de curation des tags") reste inchangé et s'applique après coup, comme
aujourd'hui.

Usage :
    python sync_daily.py [--dry-run] [--ffmpeg-location CHEMIN] [--delay-seconds N]
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

from dotenv import load_dotenv

from fetch_spotify_playlist import (
    build_track_entries,
    fetch_artist_genres,
    fetch_playlist_tracks,
    get_access_token,
)

SCRIPTS_DIR = Path(__file__).parent
PLAYLISTS_CONFIG_PATH = SCRIPTS_DIR / "playlists.json"
OUTPUT_DIR = SCRIPTS_DIR / "output"


def charger_playlists() -> list[dict]:
    if not PLAYLISTS_CONFIG_PATH.exists():
        sys.exit(f"{PLAYLISTS_CONFIG_PATH} introuvable.")

    playlists = json.loads(PLAYLISTS_CONFIG_PATH.read_text(encoding="utf-8"))
    if not playlists:
        sys.exit(
            f"{PLAYLISTS_CONFIG_PATH} est vide — ajoute au moins une playlist à suivre, ex. :\n"
            '  [{"id": "37i9dQZF1DXxxxxxx", "tags": ["disney"]}]'
        )
    return playlists


def fusionner_playlists(token: str, playlists: list[dict]) -> list[dict]:
    entries_par_id: dict[str, dict] = {}

    for config in playlists:
        playlist_id = config["id"]
        tags = config.get("tags", [])
        print(f"Playlist {playlist_id} (tags: {tags or 'aucun'})...", file=sys.stderr)

        raw_tracks = fetch_playlist_tracks(token, playlist_id)
        artist_ids = [a["id"] for t in raw_tracks for a in t["artists"]]
        genres_by_artist = fetch_artist_genres(token, artist_ids)
        entries = build_track_entries(raw_tracks, genres_by_artist, tags)

        for entry in entries:
            existante = entries_par_id.get(entry["id"])
            if existante is None:
                entries_par_id[entry["id"]] = entry
            else:
                # Morceau déjà vu dans une playlist précédente de ce lot : fusionne les tags
                # plutôt que d'écraser (ex. un titre suivi à la fois via une playlist Disney et
                # une playlist années 90).
                existante["tags"] = sorted(set(existante["tags"]) | set(entry["tags"]))

        print(f"  {len(entries)} morceaux.", file=sys.stderr)

    return list(entries_par_id.values())


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dry-run", action="store_true", help="Récupère les métadonnées mais ne télécharge aucun audio")
    parser.add_argument("--ffmpeg-location", default=None, help="Chemin vers ffmpeg si non présent sur le PATH")
    parser.add_argument("--delay-seconds", type=float, default=3.0, help="Pause entre chaque téléchargement (défaut 3s)")
    args = parser.parse_args()

    load_dotenv(SCRIPTS_DIR / ".env")
    client_id = os.environ.get("SPOTIFY_CLIENT_ID")
    client_secret = os.environ.get("SPOTIFY_CLIENT_SECRET")
    if not client_id or not client_secret:
        sys.exit("SPOTIFY_CLIENT_ID / SPOTIFY_CLIENT_SECRET manquants dans data/scripts/.env")

    playlists = charger_playlists()

    print(f"[{datetime.now(timezone.utc).isoformat()}] Synchronisation de {len(playlists)} playlist(s)...", file=sys.stderr)
    token = get_access_token(client_id, client_secret)
    entries = fusionner_playlists(token, playlists)
    print(f"{len(entries)} morceaux uniques au total.", file=sys.stderr)

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    export_path = OUTPUT_DIR / f"sync_daily_{datetime.now(timezone.utc):%Y%m%d}.json"
    export_path.write_text(json.dumps(entries, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Export fusionné écrit dans {export_path}.", file=sys.stderr)

    if args.dry_run:
        print("--dry-run : téléchargement audio sauté.", file=sys.stderr)
        return

    commande = [
        sys.executable,
        str(SCRIPTS_DIR / "download_audio.py"),
        str(export_path),
        "--delay-seconds",
        str(args.delay_seconds),
    ]
    if args.ffmpeg_location:
        commande += ["--ffmpeg-location", args.ffmpeg_location]

    resultat = subprocess.run(commande)
    sys.exit(resultat.returncode)


if __name__ == "__main__":
    main()
