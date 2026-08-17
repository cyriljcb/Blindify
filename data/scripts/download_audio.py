"""
Étape 2 du pipeline (docs/architecture.md section 3) : à partir d'un export produit par
fetch_spotify_playlist.py, télécharge l'audio depuis YouTube (via yt-dlp) et la cover
depuis Spotify pour chaque morceau, puis fusionne les entrées complètes dans
data/tracks.json (la source de vérité).

Usage :
    python download_audio.py <export.json> [--limit N] [--tolerance-seconds 8]

Validation du matching (architecture.md section 3, point 4) : après récupération des
métadonnées YouTube, la durée est comparée à durationMs (Spotify), tolérance
±tolerance-seconds. Au-delà, le morceau est flaggé "à vérifier manuellement" et n'est
NI téléchargé NI ajouté à tracks.json plutôt que d'intégrer une mauvaise version
(live, reprise, lyric video d'un autre artiste).

Reprise après interruption : les morceaux déjà présents dans tracks.json (par id) ou
dont le fichier audio existe déjà sont sautés — on peut relancer le script à tout
moment sans retélécharger ce qui est déjà fait. tracks.json est réécrit après chaque
morceau réussi (pas seulement à la fin) pour ne rien perdre en cas d'interruption sur
un gros lot.
"""

from __future__ import annotations

import argparse
import json
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

import requests
import yt_dlp

SCRIPTS_DIR = Path(__file__).parent
DATA_DIR = SCRIPTS_DIR.parent
AUDIO_DIR = DATA_DIR / "audio"
COVERS_DIR = DATA_DIR / "covers"
TRACKS_JSON_PATH = DATA_DIR / "tracks.json"


def charger_tracks_json() -> list[dict]:
    if not TRACKS_JSON_PATH.exists():
        return []
    return json.loads(TRACKS_JSON_PATH.read_text(encoding="utf-8"))


def sauvegarder_tracks_json(tracks: list[dict]) -> None:
    TRACKS_JSON_PATH.write_text(json.dumps(tracks, ensure_ascii=False, indent=2), encoding="utf-8")


def rechercher_sur_youtube(ffmpeg_location: str | None, requete: str) -> dict | None:
    """Récupère les métadonnées du premier résultat de recherche, sans télécharger."""
    options = {
        "quiet": True,
        "no_warnings": True,
        "default_search": "ytsearch1",
        "noplaylist": True,
        "skip_download": True,
    }
    if ffmpeg_location:
        options["ffmpeg_location"] = ffmpeg_location

    with yt_dlp.YoutubeDL(options) as ydl:
        resultat = ydl.extract_info(requete, download=False)
        entries = resultat.get("entries") if resultat and "entries" in resultat else [resultat]
        return entries[0] if entries else None


def telecharger_audio(ffmpeg_location: str | None, video_id: str, destination_sans_extension: Path) -> None:
    options = {
        "quiet": True,
        "no_warnings": True,
        "format": "bestaudio/best",
        "outtmpl": str(destination_sans_extension) + ".%(ext)s",
        "postprocessors": [
            {"key": "FFmpegExtractAudio", "preferredcodec": "mp3", "preferredquality": "192"},
        ],
    }
    if ffmpeg_location:
        options["ffmpeg_location"] = ffmpeg_location

    with yt_dlp.YoutubeDL(options) as ydl:
        ydl.download([f"https://www.youtube.com/watch?v={video_id}"])


def telecharger_cover(url: str, destination: Path) -> None:
    response = requests.get(url, timeout=15)
    response.raise_for_status()
    destination.write_bytes(response.content)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("export", help="Fichier JSON produit par fetch_spotify_playlist.py")
    parser.add_argument("--limit", type=int, default=None, help="Traiter seulement les N premiers morceaux (test)")
    parser.add_argument(
        "--tolerance-seconds", type=int, default=8, help="Tolérance d'écart de durée Spotify/YouTube (défaut 8s)"
    )
    parser.add_argument(
        "--ffmpeg-location", default=None, help="Chemin vers ffmpeg si non présent sur le PATH"
    )
    parser.add_argument(
        "--delay-seconds",
        type=float,
        default=3.0,
        help="Pause entre chaque morceau traité, pour limiter le risque de blocage YouTube (défaut 3s)",
    )
    args = parser.parse_args()

    entries = json.loads(Path(args.export).read_text(encoding="utf-8"))
    if args.limit:
        entries = entries[: args.limit]

    AUDIO_DIR.mkdir(parents=True, exist_ok=True)
    COVERS_DIR.mkdir(parents=True, exist_ok=True)

    tracks = charger_tracks_json()
    ids_existants = {t["id"] for t in tracks}

    a_verifier: list[dict] = []
    echecs: list[dict] = []
    traites = 0
    deja_presents = 0

    for i, entry in enumerate(entries, start=1):
        track_id = entry["id"]
        titre, artiste = entry["title"], entry["artist"]

        if track_id in ids_existants:
            deja_presents += 1
            continue

        audio_path = AUDIO_DIR / f"{track_id}.mp3"
        print(f"[{i}/{len(entries)}] {artiste} - {titre}", file=sys.stderr)

        try:
            if not audio_path.exists():
                requete = f"{artiste} {titre}"
                resultat = rechercher_sur_youtube(args.ffmpeg_location, requete)
                if resultat is None:
                    echecs.append({"id": track_id, "titre": titre, "artiste": artiste, "raison": "aucun résultat"})
                    continue

                duree_youtube_ms = (resultat.get("duration") or 0) * 1000
                ecart_s = abs(duree_youtube_ms - entry["durationMs"]) / 1000
                if ecart_s > args.tolerance_seconds:
                    a_verifier.append(
                        {
                            "id": track_id,
                            "titre": titre,
                            "artiste": artiste,
                            "ecart_secondes": round(ecart_s, 1),
                            "youtube_url": resultat.get("webpage_url"),
                        }
                    )
                    continue

                telecharger_audio(args.ffmpeg_location, resultat["id"], AUDIO_DIR / track_id)
                youtube_id = resultat["id"]
            else:
                youtube_id = entry.get("youtubeId")  # reprise : fichier déjà là, on ne re-cherche pas

            cover_path = COVERS_DIR / f"{track_id}.jpg"
            if entry.get("spotifyCoverUrl") and not cover_path.exists():
                telecharger_cover(entry["spotifyCoverUrl"], cover_path)

            tracks.append(
                {
                    "id": track_id,
                    "title": titre,
                    "artist": artiste,
                    "album": entry.get("album"),
                    "spotifyId": entry.get("spotifyId"),
                    "youtubeId": youtube_id,
                    "durationMs": entry["durationMs"],
                    "genres": entry.get("genres", []),
                    "tags": entry.get("tags", []),
                    "trapWith": [],
                    "year": entry.get("year"),
                    "filePath": f"audio/{track_id}.mp3",
                    "coverPath": f"covers/{track_id}.jpg" if cover_path.exists() else None,
                    "addedAt": datetime.now(timezone.utc).isoformat(),
                }
            )
            ids_existants.add(track_id)
            sauvegarder_tracks_json(tracks)
            traites += 1

        except Exception as e:  # noqa: BLE001 — un morceau en échec ne doit pas arrêter le lot
            print(f"  échec : {e}", file=sys.stderr)
            echecs.append({"id": track_id, "titre": titre, "artiste": artiste, "raison": str(e)})
        finally:
            # Pause entre chaque morceau ayant déclenché une requête réseau, pour limiter
            # le risque de blocage/throttling YouTube sur un gros volume de requêtes.
            time.sleep(args.delay_seconds)

    print(
        f"\nTerminé : {traites} ajoutés, {deja_presents} déjà présents, "
        f"{len(a_verifier)} à vérifier manuellement, {len(echecs)} échecs.",
        file=sys.stderr,
    )

    if a_verifier:
        rapport_path = SCRIPTS_DIR / "output" / "a_verifier.json"
        rapport_path.write_text(json.dumps(a_verifier, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"Morceaux à vérifier manuellement (écart de durée) : {rapport_path}", file=sys.stderr)

    if echecs:
        rapport_path = SCRIPTS_DIR / "output" / "echecs.json"
        rapport_path.write_text(json.dumps(echecs, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"Échecs de téléchargement : {rapport_path}", file=sys.stderr)


if __name__ == "__main__":
    main()
