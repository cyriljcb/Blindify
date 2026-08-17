"""
Récupère les morceaux d'une playlist Spotify publique (métadonnées + genres par
artiste) et écrit un export JSON intermédiaire — PAS directement data/tracks.json.

Pourquoi un fichier intermédiaire et non tracks.json directement : le schéma
tracks.json (docs/architecture.md section 4) exige un filePath (chemin audio local),
qui n'existe qu'une fois le morceau téléchargé depuis YouTube (étape suivante du
pipeline, pas encore implémentée). Écrire un tracks.json incomplet casserait le
chargement du backend (FilePath est un champ requis côté TrackDto).

Usage :
    python fetch_spotify_playlist.py <playlist_id_ou_url> [--output FICHIER] [--tags tag1,tag2]

Authentification : flow "Client Credentials" (pas de login utilisateur), valable
pour toute playlist publique. Nécessite SPOTIFY_CLIENT_ID / SPOTIFY_CLIENT_SECRET
dans un fichier .env local (voir .env.example) ou dans l'environnement.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

import requests
from dotenv import load_dotenv

TOKEN_URL = "https://accounts.spotify.com/api/token"
API_BASE = "https://api.spotify.com/v1"
ARTIST_BATCH_SIZE = 50
PLAYLIST_PAGE_SIZE = 100


def get_access_token(client_id: str, client_secret: str) -> str:
    response = requests.post(
        TOKEN_URL,
        data={"grant_type": "client_credentials"},
        auth=(client_id, client_secret),
        timeout=10,
    )
    response.raise_for_status()
    return response.json()["access_token"]


def extract_playlist_id(value: str) -> str:
    """Accepte un ID brut, un ID suivi d'un ?si=..., ou une URL open.spotify.com/playlist/<id>."""
    match = re.search(r"playlist/([a-zA-Z0-9]+)", value)
    playlist_id = match.group(1) if match else value
    return playlist_id.split("?")[0]


def _api_get(url: str, token: str, params: dict | None = None) -> dict:
    while True:
        response = requests.get(
            url, headers={"Authorization": f"Bearer {token}"}, params=params, timeout=10
        )
        if response.status_code == 429:
            retry_after = int(response.headers.get("Retry-After", "1"))
            print(f"Rate limit atteint, attente de {retry_after}s...", file=sys.stderr)
            time.sleep(retry_after)
            continue
        response.raise_for_status()
        return response.json()


def fetch_playlist_tracks(token: str, playlist_id: str) -> list[dict]:
    tracks: list[dict] = []
    seen_ids: set[str] = set()
    url = f"{API_BASE}/playlists/{playlist_id}/tracks"
    params = {
        "limit": PLAYLIST_PAGE_SIZE,
        "fields": "next,items(track(id,name,duration_ms,is_local,"
        "album(name,release_date,images),artists(id,name)))",
    }

    while url:
        data = _api_get(url, token, params=params)
        for item in data.get("items", []):
            track = item.get("track")
            if track is None or track.get("is_local") or not track.get("id"):
                continue  # morceaux indisponibles/locaux : pas d'ID Spotify exploitable
            if track["id"] in seen_ids:
                continue  # même morceau ajouté plusieurs fois à la playlist
            seen_ids.add(track["id"])
            tracks.append(track)

        url = data.get("next")
        params = None  # l'URL "next" contient déjà les query params

    return tracks


def fetch_artist_genres(token: str, artist_ids: list[str]) -> dict[str, list[str]]:
    genres_by_artist: dict[str, list[str]] = {}
    unique_ids = list(dict.fromkeys(artist_ids))  # dédupliquer en gardant l'ordre

    for i in range(0, len(unique_ids), ARTIST_BATCH_SIZE):
        batch = unique_ids[i : i + ARTIST_BATCH_SIZE]
        data = _api_get(f"{API_BASE}/artists", token, params={"ids": ",".join(batch)})
        for artist in data.get("artists", []):
            if artist:
                genres_by_artist[artist["id"]] = artist.get("genres", [])

    return genres_by_artist


def build_track_entries(
    raw_tracks: list[dict], genres_by_artist: dict[str, list[str]], tags: list[str]
) -> list[dict]:
    now = datetime.now(timezone.utc).isoformat()
    entries = []

    for track in raw_tracks:
        artist_ids = [a["id"] for a in track["artists"]]
        genres = sorted({g for aid in artist_ids for g in genres_by_artist.get(aid, [])})

        release_date = track["album"].get("release_date", "")
        year = int(release_date[:4]) if release_date[:4].isdigit() else None

        images = track["album"].get("images", [])
        cover_url = images[0]["url"] if images else None  # plus haute résolution en premier

        entries.append(
            {
                "id": track["id"],
                "title": track["name"],
                "artist": ", ".join(a["name"] for a in track["artists"]),
                "album": track["album"].get("name"),
                "spotifyId": track["id"],
                "youtubeId": None,
                "durationMs": track["duration_ms"],
                "genres": genres,
                "tags": tags,
                "trapWith": [],
                "year": year,
                "spotifyCoverUrl": cover_url,
                "addedAt": now,
            }
        )

    return entries


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("playlist", help="ID ou URL de la playlist Spotify (doit être publique)")
    parser.add_argument(
        "--output",
        default=None,
        help="Fichier JSON de sortie (défaut : data/scripts/output/<playlist_id>.json)",
    )
    parser.add_argument(
        "--tags",
        default="",
        help="Tags à préremplir pour tous les morceaux de cette playlist, séparés par des virgules",
    )
    args = parser.parse_args()

    load_dotenv(Path(__file__).parent / ".env")
    client_id = os.environ.get("SPOTIFY_CLIENT_ID")
    client_secret = os.environ.get("SPOTIFY_CLIENT_SECRET")
    if not client_id or not client_secret:
        sys.exit(
            "SPOTIFY_CLIENT_ID / SPOTIFY_CLIENT_SECRET manquants - copie .env.example en .env "
            "et renseigne tes identifiants (https://developer.spotify.com/dashboard)."
        )

    playlist_id = extract_playlist_id(args.playlist)
    tags = [t.strip() for t in args.tags.split(",") if t.strip()]

    print(f"Authentification Spotify...", file=sys.stderr)
    token = get_access_token(client_id, client_secret)

    print(f"Récupération des morceaux de la playlist {playlist_id}...", file=sys.stderr)
    raw_tracks = fetch_playlist_tracks(token, playlist_id)
    print(f"{len(raw_tracks)} morceaux trouvés.", file=sys.stderr)

    artist_ids = [a["id"] for t in raw_tracks for a in t["artists"]]
    print(f"Récupération des genres pour {len(set(artist_ids))} artistes uniques...", file=sys.stderr)
    genres_by_artist = fetch_artist_genres(token, artist_ids)

    entries = build_track_entries(raw_tracks, genres_by_artist, tags)

    output_path = (
        Path(args.output)
        if args.output
        else Path(__file__).parent / "output" / f"{playlist_id}.json"
    )
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(entries, ensure_ascii=False, indent=2), encoding="utf-8")

    print(f"Export écrit dans {output_path} ({len(entries)} morceaux).", file=sys.stderr)


if __name__ == "__main__":
    main()
