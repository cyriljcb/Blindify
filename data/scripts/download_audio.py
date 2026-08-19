"""
Étape 2 du pipeline (docs/architecture.md section 3) : à partir d'un export produit par
fetch_spotify_playlist.py, télécharge l'audio depuis YouTube (via yt-dlp) et la cover
depuis Spotify pour chaque morceau, égalise le niveau sonore (ffmpeg loudnorm) et devine
le point de départ du refrain (pychorus, voir refrainStartMs dans architecture.md section 4),
puis fusionne les entrées complètes dans data/tracks.json (la source de vérité).

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
import os
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

import requests
import yt_dlp
from pychorus import find_and_output_chorus

SCRIPTS_DIR = Path(__file__).parent
DATA_DIR = SCRIPTS_DIR.parent
AUDIO_DIR = DATA_DIR / "audio"
COVERS_DIR = DATA_DIR / "covers"
TRACKS_JSON_PATH = DATA_DIR / "tracks.json"

# Cible de normalisation (EBU R128, standard streaming) — retour utilisateur : le volume
# variait trop d'un morceau a l'autre pendant une partie.
_LOUDNORM_I = "-16"
_LOUDNORM_LRA = "11"
_LOUDNORM_TP = "-1.5"

# Duree minimum de piste qu'il doit rester apres le refrain pour que la lecture au round
# (fenetre de reponse) ait le temps de se dérouler entierement.
_REFRAIN_MARGE_FIN_SEC = 20
_REFRAIN_CLIP_LENGTH_SEC = 15


def charger_tracks_json() -> list[dict]:
    if not TRACKS_JSON_PATH.exists():
        return []
    return json.loads(TRACKS_JSON_PATH.read_text(encoding="utf-8"))


def sauvegarder_tracks_json(tracks: list[dict]) -> None:
    TRACKS_JSON_PATH.write_text(json.dumps(tracks, ensure_ascii=False, indent=2), encoding="utf-8")


_RECHERCHE_NB_CANDIDATS = 5


def rechercher_sur_youtube(ffmpeg_location: str | None, requete: str) -> list[dict]:
    """Récupère les métadonnées des premiers résultats de recherche, sans télécharger.

    Plusieurs candidats (pas juste le premier résultat) car les clips YouTube officiels
    ont souvent une intro/outro différente de la version streaming Spotify — se limiter
    au 1er résultat faisait flaguer "à vérifier" la majorité des morceaux (retour
    playtest sur un lot de 20 : 13/20 alors que c'étaient les bonnes vidéos officielles).
    """
    options = {
        "quiet": True,
        "no_warnings": True,
        "default_search": f"ytsearch{_RECHERCHE_NB_CANDIDATS}",
        "noplaylist": True,
        "skip_download": True,
    }
    if ffmpeg_location:
        options["ffmpeg_location"] = ffmpeg_location

    with yt_dlp.YoutubeDL(options) as ydl:
        resultat = ydl.extract_info(requete, download=False)
        entries = resultat.get("entries") if resultat and "entries" in resultat else [resultat]
        return [e for e in entries if e]


def meilleur_candidat(candidats: list[dict], duree_ms_attendue: int) -> tuple[dict, float]:
    """Choisit, parmi les résultats de recherche, celui dont la durée colle le mieux à
    la durée Spotify. Retourne le candidat et son écart en secondes."""
    meilleur = min(
        candidats, key=lambda c: abs((c.get("duration") or 0) * 1000 - duree_ms_attendue)
    )
    ecart_s = abs((meilleur.get("duration") or 0) * 1000 - duree_ms_attendue) / 1000
    return meilleur, ecart_s


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


def _chemin_ffmpeg(ffmpeg_location: str | None) -> str:
    if not ffmpeg_location:
        return "ffmpeg"
    return str(Path(ffmpeg_location) / ("ffmpeg.exe" if os.name == "nt" else "ffmpeg"))


def normaliser_volume(chemin_mp3: Path, ffmpeg_location: str | None) -> None:
    """Egalise le niveau sonore (loudnorm EBU R128, deux passes) — sans ca, le volume
    variait trop d'un morceau a l'autre pendant une partie (retour utilisateur)."""
    ffmpeg = _chemin_ffmpeg(ffmpeg_location)
    filtre_mesure = f"loudnorm=I={_LOUDNORM_I}:LRA={_LOUDNORM_LRA}:TP={_LOUDNORM_TP}:print_format=json"

    mesure = subprocess.run(
        [ffmpeg, "-i", str(chemin_mp3), "-af", filtre_mesure, "-f", "null", "-"],
        capture_output=True, text=True, check=True,
    )
    bloc_json = mesure.stderr[mesure.stderr.rindex("{"): mesure.stderr.rindex("}") + 1]
    stats = json.loads(bloc_json)

    filtre_application = (
        f"loudnorm=I={_LOUDNORM_I}:LRA={_LOUDNORM_LRA}:TP={_LOUDNORM_TP}:"
        f"measured_I={stats['input_i']}:measured_LRA={stats['input_lra']}:"
        f"measured_TP={stats['input_tp']}:measured_thresh={stats['input_thresh']}:"
        f"offset={stats['target_offset']}:linear=true"
    )

    chemin_tmp = chemin_mp3.with_suffix(".normalise.mp3")
    subprocess.run(
        [ffmpeg, "-y", "-i", str(chemin_mp3), "-af", filtre_application,
         "-ar", "44100", "-c:a", "libmp3lame", "-q:a", "2", str(chemin_tmp)],
        capture_output=True, text=True, check=True,
    )
    chemin_tmp.replace(chemin_mp3)


def estimer_refrain_start_ms(chemin_mp3: Path, duree_ms: int) -> int | None:
    """Devine le point de depart du refrain par analyse de similarite audio (pychorus —
    trouve la section la plus repetee, meme principe que les outils karaoke/DJ). None si
    rien de fiable trouve, ou si le passage detecte laisserait trop peu de morceau apres
    lui pour dérouler une fenetre de reponse complete — a affiner manuellement dans
    tracks.json si le resultat ne convient pas (voir architecture.md section 4)."""
    try:
        debut_sec = find_and_output_chorus(str(chemin_mp3), None, clip_length=_REFRAIN_CLIP_LENGTH_SEC)
    except Exception as e:  # noqa: BLE001 — une estimation ratee ne doit pas bloquer l'import
        print(f"  estimation du refrain impossible : {e}", file=sys.stderr)
        return None

    if debut_sec is None:
        return None

    duree_sec = duree_ms / 1000
    if duree_sec - debut_sec < _REFRAIN_MARGE_FIN_SEC:
        return None

    return round(debut_sec * 1000)


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
                candidats = rechercher_sur_youtube(args.ffmpeg_location, requete)
                if not candidats:
                    echecs.append({"id": track_id, "titre": titre, "artiste": artiste, "raison": "aucun résultat"})
                    continue

                resultat, ecart_s = meilleur_candidat(candidats, entry["durationMs"])
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
                normaliser_volume(audio_path, args.ffmpeg_location)
                youtube_id = resultat["id"]
            else:
                youtube_id = entry.get("youtubeId")  # reprise : fichier déjà là, on ne re-cherche pas

            cover_path = COVERS_DIR / f"{track_id}.jpg"
            if entry.get("spotifyCoverUrl") and not cover_path.exists():
                telecharger_cover(entry["spotifyCoverUrl"], cover_path)

            refrain_start_ms = estimer_refrain_start_ms(audio_path, entry["durationMs"])

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
                    "refrainStartMs": refrain_start_ms,
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
