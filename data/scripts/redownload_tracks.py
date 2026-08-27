"""
Re-télécharge l'audio de morceaux DÉJÀ présents dans tracks.json — corrige un mauvais match
repéré après coup (ex. version live/karaoke/instrumentale téléchargée par erreur au premier
passage, voir audit_live_versions.py). download_audio.py refuse volontairement de retoucher un
morceau déjà présent (reprise après interruption, voir sa docstring) — ce script comble ce trou.

Réutilise la même recherche/sélection que download_audio.py (rechercher_sur_youtube,
meilleur_candidat — écarte les candidats live/acoustique/remix/cover en priorité, voir
live_keywords.py) sur une NOUVELLE recherche YouTube (ignore l'ancien youtubeId), remplace le
fichier audio existant, ré-estime refrainStartMs, et met à jour uniquement youtubeId/
refrainStartMs dans tracks.json — title/artist/year/tags/genres/... inchangés.

Usage :
    python redownload_tracks.py ID1 ID2 ... [--tolerance-seconds 8] [--delay-seconds 3]
    python redownload_tracks.py --csv CHEMIN.csv [--tolerance-seconds 8]   (colonne "id")

Si le meilleur candidat reste suspect (aucune option propre trouvée) ou dépasse la tolérance de
durée, le morceau est laissé TEL QUEL (ancien fichier conservé) et listé en sortie — jamais de
remplacement par une version pire que l'actuelle.
"""

from __future__ import annotations

import argparse
import csv
import sys
import time
from pathlib import Path

from download_audio import (
    AUDIO_DIR,
    charger_tracks_json,
    estimer_refrain_start_ms,
    meilleur_candidat,
    normaliser_volume,
    rechercher_sur_youtube,
    sauvegarder_tracks_json,
    telecharger_audio,
)
from live_keywords import mots_suspects


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("ids", nargs="*", help="Ids de morceaux (tracks.json) à re-télécharger")
    parser.add_argument("--csv", default=None, help="CSV avec une colonne 'id' (ex. sortie de audit_live_versions.py)")
    parser.add_argument("--tolerance-seconds", type=int, default=8, help="Tolérance d'écart de durée Spotify/YouTube (défaut 8s)")
    parser.add_argument("--ffmpeg-location", default=None, help="Chemin vers ffmpeg si non présent sur le PATH")
    parser.add_argument("--delay-seconds", type=float, default=3.0, help="Pause entre chaque morceau (défaut 3s)")
    args = parser.parse_args()

    ids = list(args.ids)
    if args.csv:
        with open(args.csv, encoding="utf-8-sig", newline="") as f:
            ids.extend(row["id"] for row in csv.DictReader(f))
    if not ids:
        sys.exit("Aucun id fourni (arguments positionnels ou --csv).")

    tracks = charger_tracks_json()
    tracks_par_id = {t["id"]: t for t in tracks}

    ids_inconnus = [i for i in ids if i not in tracks_par_id]
    if ids_inconnus:
        print(f"ATTENTION : id(s) introuvable(s) dans tracks.json, ignoré(s) : {', '.join(ids_inconnus)}", file=sys.stderr)
    ids = [i for i in ids if i in tracks_par_id]

    reussis, toujours_suspects, echecs = [], [], []

    for i, track_id in enumerate(ids, start=1):
        track = tracks_par_id[track_id]
        titre, artiste = track["title"], track["artist"]
        print(f"[{i}/{len(ids)}] {artiste} - {titre}", file=sys.stderr)

        try:
            requete = f"{artiste} {titre}"
            candidats = rechercher_sur_youtube(args.ffmpeg_location, requete)
            if not candidats:
                echecs.append(track_id)
                print("  échec : aucun résultat", file=sys.stderr)
                continue

            resultat, ecart_s = meilleur_candidat(candidats, track["durationMs"], titre)
            encore_suspect = mots_suspects(resultat.get("title") or "", titre)

            if encore_suspect or ecart_s > args.tolerance_seconds:
                toujours_suspects.append({"id": track_id, "titre": titre, "artiste": artiste,
                                           "youtube_title": resultat.get("title"), "ecart_secondes": round(ecart_s, 1)})
                print(f"  aucune option propre trouvée (suspect={bool(encore_suspect)}, écart={ecart_s:.1f}s) — fichier conservé tel quel", file=sys.stderr)
                continue

            audio_path = AUDIO_DIR / f"{track_id}.mp3"
            audio_path.unlink(missing_ok=True)  # l'ancien fichier ne doit pas fausser la reprise de download_audio.py
            telecharger_audio(args.ffmpeg_location, resultat["id"], AUDIO_DIR / track_id)
            normaliser_volume(audio_path, args.ffmpeg_location)

            track["youtubeId"] = resultat["id"]
            track["refrainStartMs"] = estimer_refrain_start_ms(audio_path, track["durationMs"])
            sauvegarder_tracks_json(tracks)
            reussis.append(track_id)
            print(f"  remplacé : {resultat.get('title')}", file=sys.stderr)

        except Exception as e:  # noqa: BLE001 — un échec ne doit pas arrêter le lot
            echecs.append(track_id)
            print(f"  échec : {e}", file=sys.stderr)
        finally:
            time.sleep(args.delay_seconds)

    print(f"\nTerminé : {len(reussis)} remplacé(s), {len(toujours_suspects)} toujours suspect(s) "
          f"(fichier conservé), {len(echecs)} échec(s).", file=sys.stderr)
    if toujours_suspects:
        for s in toujours_suspects:
            print(f"  à revoir manuellement : {s['artiste']} - {s['titre']} (youtube: {s['youtube_title']!r})", file=sys.stderr)


if __name__ == "__main__":
    main()
