"""
Audit du catalogue à la recherche de morceaux dont `year` provient d'une réédition/remaster
plutôt que de la sortie originale (retour utilisateur 2026-08-27 : "Don't You (Forget About
Me)" de Simple Minds — sortie 1985 — se retrouvait tagué "années-2020" avec year=2025,
vraisemblablement la date d'une réédition 40e anniversaire).

Cause : fetch_spotify_playlist.py lit `year` depuis `album.release_date` de l'ALBUM associé au
morceau sur Spotify — pour un morceau ajouté via une compilation/réédition récente, cette date
n'a aucun rapport avec la sortie originale.

Pour chaque morceau, cherche toutes les correspondances Spotify (titre+artiste) et retient
l'année la plus ancienne trouvée parmi les albums correspondants. Si elle est nettement
antérieure à `year` (tracks.json), exporte en CSV pour relecture manuelle — comme
export_tags_csv.py, ne modifie jamais tracks.json.

Usage :
    python audit_reissue_years.py [--limit N] [--seuil-ecart-annees 5] [--delay-seconds 0.3]

Résultats mis en cache (output/reissue_audit_cache.json) et réutilisés d'un run à l'autre —
--refresh ignore le cache et re-vérifie tout.
"""

from __future__ import annotations

import argparse
import csv
import json
import os
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

from dotenv import load_dotenv

from fetch_spotify_playlist import _api_get, get_access_token, nettoyer_titre

SCRIPTS_DIR = Path(__file__).parent
DATA_DIR = SCRIPTS_DIR.parent
TRACKS_JSON_PATH = DATA_DIR / "tracks.json"
OUTPUT_DIR = SCRIPTS_DIR / "output"
CACHE_PATH = OUTPUT_DIR / "reissue_audit_cache.json"
API_BASE = "https://api.spotify.com/v1"

CSV_FIELDS = ["id", "title", "artist", "year_actuel", "year_probable", "ecart_annees", "album_le_plus_ancien"]

DEFAULT_SEUIL_ECART_ANNEES = 5


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


def premier_artiste(artist_field: str) -> str:
    """tracks.json stocke les artistes joints par ', ' (voir fetch_spotify_playlist.build_track_entries)."""
    return artist_field.split(",")[0].strip()


def rechercher_albums(token: str, title: str, artist: str) -> list[dict]:
    """Cherche le morceau sur Spotify et retourne les (nom_album, année) de chaque résultat dont
    le titre nettoyé et l'artiste correspondent — pour repérer, parmi toutes les éditions
    connues de Spotify, l'année de sortie la plus ancienne."""
    query = f'track:"{title}" artist:"{artist}"'
    data = _api_get(f"{API_BASE}/search", token, params={"q": query, "type": "track", "limit": 50})
    resultats = []
    titre_normalise = nettoyer_titre(title).casefold()
    artiste_normalise = artist.casefold()

    for item in data.get("tracks", {}).get("items", []):
        item_titre = nettoyer_titre(item.get("name", "")).casefold()
        item_artistes = [a["name"].casefold() for a in item.get("artists", [])]
        if item_titre != titre_normalise:
            continue
        if not any(artiste_normalise == a or artiste_normalise in a or a in artiste_normalise for a in item_artistes):
            continue

        release_date = item.get("album", {}).get("release_date", "")
        if release_date[:4].isdigit():
            resultats.append((item["album"].get("name", ""), int(release_date[:4])))

    return resultats


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--limit", type=int, default=None, help="Ne traiter que les N premiers morceaux (test)")
    parser.add_argument("--seuil-ecart-annees", type=int, default=DEFAULT_SEUIL_ECART_ANNEES,
                         help=f"Écart minimum (années) pour flagger un morceau (défaut {DEFAULT_SEUIL_ECART_ANNEES})")
    parser.add_argument("--delay-seconds", type=float, default=0.3, help="Pause entre chaque requête Spotify (défaut 0.3s)")
    parser.add_argument("--refresh", action="store_true", help="Ignore le cache, re-vérifie tous les morceaux")
    parser.add_argument("--out", default=None, help="Chemin du CSV en sortie (défaut : data/scripts/output/reissue_years_AAAAMMJJ.csv)")
    args = parser.parse_args()

    load_dotenv(SCRIPTS_DIR / ".env")
    client_id = os.environ.get("SPOTIFY_CLIENT_ID")
    client_secret = os.environ.get("SPOTIFY_CLIENT_SECRET")
    if not client_id or not client_secret:
        sys.exit(
            "SPOTIFY_CLIENT_ID / SPOTIFY_CLIENT_SECRET manquants - copie .env.example en .env "
            "et renseigne tes identifiants (https://developer.spotify.com/dashboard)."
        )

    tracks = [t for t in charger_tracks_json() if t.get("year")]
    if args.limit:
        tracks = tracks[: args.limit]

    print("Authentification Spotify...", file=sys.stderr)
    token = get_access_token(client_id, client_secret)

    cache = {} if args.refresh else charger_cache()

    for i, track in enumerate(tracks, start=1):
        track_id = track["id"]
        if track_id in cache:
            continue

        artist = premier_artiste(track.get("artist", ""))
        print(f"[{i}/{len(tracks)}] {artist} - {track.get('title')}", file=sys.stderr)
        try:
            albums = rechercher_albums(token, track.get("title", ""), artist)
            cache[track_id] = {"albums": albums, "error": None}
        except Exception as e:  # noqa: BLE001 — un échec ne doit pas arrêter le lot
            print(f"  échec : {e}", file=sys.stderr)
            cache[track_id] = {"albums": [], "error": str(e)}
        finally:
            sauvegarder_cache(cache)
            time.sleep(args.delay_seconds)

    lignes_suspectes = []
    for track in tracks:
        entree = cache.get(track["id"])
        if not entree or not entree.get("albums"):
            continue

        album_le_plus_ancien, annee_probable = min(entree["albums"], key=lambda a: a[1])
        year_actuel = track["year"]
        ecart = year_actuel - annee_probable
        if ecart >= args.seuil_ecart_annees:
            lignes_suspectes.append(
                {
                    "id": track["id"],
                    "title": track.get("title", ""),
                    "artist": track.get("artist", ""),
                    "year_actuel": year_actuel,
                    "year_probable": annee_probable,
                    "ecart_annees": ecart,
                    "album_le_plus_ancien": album_le_plus_ancien,
                }
            )

    echecs = sum(1 for v in cache.values() if v.get("error"))

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    out_path = Path(args.out) if args.out else OUTPUT_DIR / f"reissue_years_{datetime.now(timezone.utc):%Y%m%d}.csv"

    with out_path.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=CSV_FIELDS)
        writer.writeheader()
        writer.writerows(lignes_suspectes)

    print(
        f"\nTerminé : {len(tracks)} morceau(x) vérifié(s) (cache inclus), "
        f"{len(lignes_suspectes)} suspect(s) exporté(s) dans {out_path}, {echecs} échec(s) de recherche.",
        file=sys.stderr,
    )
    print(
        "Relis 'year_probable' / 'album_le_plus_ancien' : un faux positif (homonyme, reprise "
        "légitime différente) se règle en ignorant la ligne. Pour un vrai cas, corrige 'year' "
        "(et le tag décennie associé si tu en as un) directement dans tracks.json ou via le "
        "tableur habituel — ce script ne modifie rien lui-même.",
        file=sys.stderr,
    )


if __name__ == "__main__":
    main()
