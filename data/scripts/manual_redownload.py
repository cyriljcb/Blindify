"""
Redécharge l'audio d'un morceau déjà présent dans tracks.json en imposant l'ID YouTube
choisi à la main — sans recherche automatique. Complète redownload_tracks.py (qui relance
une recherche automatique) pour les cas où celle-ci échoue à trouver un candidat propre
malgré tolérance/mots-clés (retour utilisateur : quelques morceaux de l'audit
audit_live_versions.py restés "à revoir manuellement" faute de correspondance automatique
fiable — vérification manuelle ponctuelle, ID YouTube choisi après recherche externe).

Usage :
    python manual_redownload.py TRACK_ID YOUTUBE_ID
    python manual_redownload.py TRACK_ID YOUTUBE_ID --spotify-id NOUVEAU_SPOTIFY_ID

Si YOUTUBE_ID commence par un tiret (rare mais possible, ex. "-rzpX12fJy0"), argparse le
prend pour une option — place les arguments positionnels après un "--" :
    python manual_redownload.py --spotify-id ID -- TRACK_ID -rzpX12fJy0

--spotify-id : à fournir seulement si le morceau Spotify importé à l'origine était
lui-même une mauvaise version (live/compilation, voir audit_live_versions.py — ex. un
titre dont la seule entrée Spotify disponible est un album live). Met à jour spotifyId/
album/durationMs/year/spotifyCoverUrl (et retélécharge la cover) depuis ce nouveau
morceau ; sans cette option, durationMs n'est pas vérifié — on fait confiance à la
recherche manuelle. L'`id` du morceau (utilisé pour filePath/coverPath/stats.json) n'est
JAMAIS modifié, même si spotifyId change : c'est un identifiant stable indépendant de sa
valeur d'origine.
"""

from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

from dotenv import load_dotenv

from download_audio import (
    AUDIO_DIR,
    COVERS_DIR,
    charger_tracks_json,
    estimer_refrain_start_ms,
    normaliser_volume,
    sauvegarder_tracks_json,
    telecharger_audio,
    telecharger_cover,
)
from fetch_spotify_playlist import _api_get, get_access_token

SCRIPTS_DIR = Path(__file__).parent


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("track_id", help="id du morceau dans tracks.json")
    parser.add_argument("youtube_id", help="ID de la vidéo YouTube choisie manuellement")
    parser.add_argument("--spotify-id", default=None, help="Remplace aussi la source Spotify (voir docstring)")
    parser.add_argument("--ffmpeg-location", default=None, help="Chemin vers ffmpeg si non présent sur le PATH")
    args = parser.parse_args()

    tracks = charger_tracks_json()
    track = next((t for t in tracks if t["id"] == args.track_id), None)
    if track is None:
        sys.exit(f"Id introuvable dans tracks.json : {args.track_id}")

    if args.spotify_id:
        load_dotenv(SCRIPTS_DIR / ".env")
        client_id = os.environ.get("SPOTIFY_CLIENT_ID")
        client_secret = os.environ.get("SPOTIFY_CLIENT_SECRET")
        if not client_id or not client_secret:
            sys.exit("SPOTIFY_CLIENT_ID / SPOTIFY_CLIENT_SECRET manquants - voir .env.example.")

        token = get_access_token(client_id, client_secret)
        data = _api_get(f"https://api.spotify.com/v1/tracks/{args.spotify_id}", token)

        release_date = data["album"].get("release_date", "")
        images = data["album"].get("images", [])

        track["spotifyId"] = data["id"]
        track["album"] = data["album"].get("name")
        track["durationMs"] = data["duration_ms"]
        if release_date[:4].isdigit():
            track["year"] = int(release_date[:4])
        if images:
            track["spotifyCoverUrl"] = images[0]["url"]
            cover_path = COVERS_DIR / f"{track['id']}.jpg"
            telecharger_cover(track["spotifyCoverUrl"], cover_path)
            print(f"Cover mise à jour : {cover_path}", file=sys.stderr)

        print(f"Source Spotify remplacée : album={track['album']!r}, durationMs={track['durationMs']}", file=sys.stderr)

    audio_path = AUDIO_DIR / f"{track['id']}.mp3"
    audio_path.unlink(missing_ok=True)  # l'ancien fichier ne doit pas fausser la reprise de download_audio.py
    telecharger_audio(args.ffmpeg_location, args.youtube_id, AUDIO_DIR / track["id"])
    normaliser_volume(audio_path, args.ffmpeg_location)

    track["youtubeId"] = args.youtube_id
    track["refrainStartMs"] = estimer_refrain_start_ms(audio_path, track["durationMs"])
    sauvegarder_tracks_json(tracks)

    print(f"Terminé : {track['artist']} - {track['title']} → youtubeId={args.youtube_id}", file=sys.stderr)


if __name__ == "__main__":
    main()
