"""
Audit alternatif de audit_genres.py : au lieu de repartir des genres Spotify de
l'ARTISTE (ambigus en cas d'homonymie - ex. un groupe "Chorus" français vs un groupe
dévotionnel indien du même nom, qui polluent le bucketing), recoupe chaque morceau vers
Deezer via son ISRC (code unique par enregistrement, pas de recherche par nom). Le
catalogue Deezer est présumé plus fiable sur la variété française, angle mort connu du
bucketing actuel (docs/architecture.md section 3 point 3).

Pour chaque morceau : récupère l'ISRC via Spotify (déjà présent dans la réponse standard
de GET /tracks), retrouve le morceau correspondant sur Deezer (GET /track/isrc:{isrc}),
remonte à son album pour lire les genres Deezer (GET /album/{id}) et les fait passer dans
un bucketing par mots-clés adapté à la taxonomie réelle de Deezer (25 genres top-niveau,
bien plus grossière que les genres Spotify par artiste - voir DEEZER_BUCKETS).

Le CSV exporté compare trois signaux par morceau : les tags actuels, la suggestion
Spotify (relit le cache déjà produit par audit_genres.py, ne re-télécharge rien) et la
nouvelle suggestion Deezer. Les cas où Spotify ET Deezer s'accordent pour contredire le
tag actuel sont les corrections les plus sûres (signalées à part en résumé) : deux
recoupements indépendants qui pointent dans la même direction.

Comme audit_genres.py : jamais réinjecté automatiquement, jamais d'écriture dans
tracks.json, colonne "tags" réimportable telle quelle via import_tags_csv.py après
relecture (décennies/ad hoc préservés).

Usage :
    python audit_genres_deezer.py [--limit N] [--delay-seconds 0.3]

Résultats mis en cache (output/genre_deezer_cache.json), --refresh pour tout re-vérifier.
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

import requests
from dotenv import load_dotenv

from audit_genres import DECADE_TAGS, TAGS_AD_HOC, suggerer_tags
from fetch_spotify_playlist import _api_get, get_access_token

SCRIPTS_DIR = Path(__file__).parent
DATA_DIR = SCRIPTS_DIR.parent
TRACKS_JSON_PATH = DATA_DIR / "tracks.json"
OUTPUT_DIR = SCRIPTS_DIR / "output"
CACHE_PATH = OUTPUT_DIR / "genre_deezer_cache.json"
SPOTIFY_GENRE_CACHE_PATH = OUTPUT_DIR / "genre_audit_cache.json"  # produit par audit_genres.py
SPOTIFY_API_BASE = "https://api.spotify.com/v1"
DEEZER_API_BASE = "https://api.deezer.com"
SPOTIFY_BATCH_SIZE = 50
DEEZER_DELAY_SECONDS = 0.15  # Deezer public : ~50 req/5s, largement respecté avec cette pause

CSV_FIELDS = [
    "id",
    "title",
    "artist",
    "tags_actuels",
    "isrc",
    "deezer_genres",
    "suggestion_spotify",
    "suggestion_deezer",
    "accord_spotify_deezer",
    "tags",
]

# Construit par inspection réelle de GET https://api.deezer.com/genre (25 genres
# top-niveau, une seule fois par album - bien plus grossier que les genres Spotify par
# artiste) : All, Pop, Rap/Hip Hop, Rock, Dance, R&B, Alternative, Electro, Folk, Reggae,
# Jazz, French Chanson, Classical, Films/Games, Metal, Soul & Funk, Nederlandstalige
# muziek, African Music, Arabic Music, Asian Music, Blues, Brazilian Music, Indian Music,
# Kids, Latin Music. Mots-clés cherchés en sous-chaîne (insensible à la casse), même
# principe que BUCKETS dans audit_genres.py - un genre Deezer peut ne matcher aucun
# bucket (Alternative, Blues, Classical, Dance, Folk, Films/Games, Kids, Nederlandstalige
# muziek : volontairement laissés de côté, trop génériques ou hors périmètre des 9
# buckets jouables plutôt que de forcer un mauvais classement).
DEEZER_BUCKETS: dict[str, list[str]] = {
    "metal": ["metal"],
    "rock": ["rock"],
    "pop": ["pop"],
    "electro": ["electro"],  # "Dance" exclu : englobe aussi la pop dansante chez Deezer
    "rap": ["rap", "hip hop"],
    "rnb-funk-jazz": ["r&b", "soul", "funk", "jazz"],
    "variete-francaise": ["chanson"],  # "French Chanson" est un genre Deezer explicite et fiable
    "latino": ["latin"],
    "monde": ["african", "arabic", "asian", "indian", "brazilian", "reggae"],
}


def charger_tracks_json() -> list[dict]:
    if not TRACKS_JSON_PATH.exists():
        sys.exit(f"{TRACKS_JSON_PATH} introuvable.")
    return json.loads(TRACKS_JSON_PATH.read_text(encoding="utf-8"))


def charger_cache() -> dict:
    if not CACHE_PATH.exists():
        return {"isrc_by_track": {}, "deezer_track_by_isrc": {}, "genres_by_album": {}}
    return json.loads(CACHE_PATH.read_text(encoding="utf-8"))


def sauvegarder_cache(cache: dict) -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    CACHE_PATH.write_text(json.dumps(cache, ensure_ascii=False, indent=2), encoding="utf-8")


def suggerer_tags_deezer(genres: list[str]) -> list[str]:
    genres_normalises = [g.casefold() for g in genres]
    suggestions = []
    for bucket, mots_cles in DEEZER_BUCKETS.items():
        if any(mot in g for g in genres_normalises for mot in mots_cles):
            suggestions.append(bucket)
    return suggestions


def _deezer_get(url: str) -> dict | None:
    """GET Deezer public. Retourne None si la ressource est introuvable (ex. ISRC inconnu
    - erreur "DataException"/code 800, une réponse normale chez Deezer, pas une panne)."""
    while True:
        response = requests.get(url, timeout=10)
        response.raise_for_status()
        data = response.json()
        if isinstance(data, dict) and "error" in data:
            erreur = data["error"]
            if erreur.get("code") == 4:  # quota dépassé ("TooManyRequests")
                print("Rate limit Deezer atteint, attente de 5s...", file=sys.stderr)
                time.sleep(5)
                continue
            return None
        return data


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--limit", type=int, default=None, help="Ne traiter que les N premiers morceaux (test)")
    parser.add_argument("--delay-seconds", type=float, default=0.3, help="Pause entre chaque requête Spotify par lot (défaut 0.3s)")
    parser.add_argument("--refresh", action="store_true", help="Ignore le cache, re-vérifie tous les morceaux")
    parser.add_argument("--out", default=None, help="Chemin du CSV en sortie (défaut : data/scripts/output/genres_deezer_AAAAMMJJ.csv)")
    args = parser.parse_args()

    load_dotenv(SCRIPTS_DIR / ".env")
    client_id = os.environ.get("SPOTIFY_CLIENT_ID")
    client_secret = os.environ.get("SPOTIFY_CLIENT_SECRET")
    if not client_id or not client_secret:
        sys.exit(
            "SPOTIFY_CLIENT_ID / SPOTIFY_CLIENT_SECRET manquants - copie .env.example en .env "
            "et renseigne tes identifiants (https://developer.spotify.com/dashboard)."
        )

    tracks = [t for t in charger_tracks_json() if t.get("spotifyId")]
    if args.limit:
        tracks = tracks[: args.limit]

    cache = {"isrc_by_track": {}, "deezer_track_by_isrc": {}, "genres_by_album": {}} if args.refresh else charger_cache()

    # Étape 1 : ISRC par morceau (batch de 50 spotifyId via GET /tracks - external_ids.isrc
    # est déjà dans la réponse standard, pas de paramètre spécial à passer).
    print("Authentification Spotify...", file=sys.stderr)
    token = get_access_token(client_id, client_secret)

    a_recuperer = [t for t in tracks if t["id"] not in cache["isrc_by_track"]]
    print(f"{len(a_recuperer)} morceau(x) à interroger pour l'ISRC (sur {len(tracks)}, cache inclus)...", file=sys.stderr)
    for i in range(0, len(a_recuperer), SPOTIFY_BATCH_SIZE):
        batch = a_recuperer[i : i + SPOTIFY_BATCH_SIZE]
        print(f"[isrc {i + len(batch)}/{len(a_recuperer)}]", file=sys.stderr)
        data = _api_get(f"{SPOTIFY_API_BASE}/tracks", token, params={"ids": ",".join(t["spotifyId"] for t in batch)})
        for track, item in zip(batch, data.get("tracks", [])):
            isrc = (item.get("external_ids") or {}).get("isrc") if item else None
            cache["isrc_by_track"][track["id"]] = isrc
        sauvegarder_cache(cache)
        time.sleep(args.delay_seconds)

    isrcs_uniques = sorted({isrc for isrc in cache["isrc_by_track"].values() if isrc})
    print(f"{len(isrcs_uniques)} ISRC unique(s) sur {len(tracks)} morceau(x).", file=sys.stderr)

    # Étape 2 : recoupement ISRC -> morceau Deezer -> album (pour en tirer les genres).
    a_resoudre = [isrc for isrc in isrcs_uniques if isrc not in cache["deezer_track_by_isrc"]]
    print(f"{len(a_resoudre)} ISRC à recouper sur Deezer (sur {len(isrcs_uniques)}, cache inclus)...", file=sys.stderr)
    for i, isrc in enumerate(a_resoudre, start=1):
        if i % 50 == 0 or i == len(a_resoudre):
            print(f"[deezer track {i}/{len(a_resoudre)}]", file=sys.stderr)
        data = _deezer_get(f"{DEEZER_API_BASE}/track/isrc:{isrc}")
        album_id = data.get("album", {}).get("id") if data else None
        cache["deezer_track_by_isrc"][isrc] = {"found": data is not None, "album_id": album_id}
        if i % 50 == 0:
            sauvegarder_cache(cache)
        time.sleep(DEEZER_DELAY_SECONDS)
    sauvegarder_cache(cache)

    # Étape 3 : genres par album (un seul appel par album, même si plusieurs morceaux du
    # catalogue partagent le même album).
    album_ids_uniques = sorted(
        {
            str(entree["album_id"])
            for entree in cache["deezer_track_by_isrc"].values()
            if entree.get("found") and entree.get("album_id") is not None
        }
    )
    a_resoudre_albums = [aid for aid in album_ids_uniques if aid not in cache["genres_by_album"]]
    print(f"{len(a_resoudre_albums)} album(s) Deezer à interroger (sur {len(album_ids_uniques)}, cache inclus)...", file=sys.stderr)
    for i, album_id in enumerate(a_resoudre_albums, start=1):
        if i % 50 == 0 or i == len(a_resoudre_albums):
            print(f"[deezer album {i}/{len(a_resoudre_albums)}]", file=sys.stderr)
        data = _deezer_get(f"{DEEZER_API_BASE}/album/{album_id}")
        genres = [g["name"] for g in (data.get("genres", {}).get("data", []) if data else [])]
        cache["genres_by_album"][album_id] = genres
        if i % 50 == 0:
            sauvegarder_cache(cache)
        time.sleep(DEEZER_DELAY_SECONDS)
    sauvegarder_cache(cache)

    # Étape 4 : suggestion Spotify - relit le cache déjà produit par audit_genres.py (pas de
    # nouvel appel Spotify /artists ici, cet audit se concentre sur le signal Deezer).
    if not SPOTIFY_GENRE_CACHE_PATH.exists():
        sys.exit(
            f"{SPOTIFY_GENRE_CACHE_PATH} introuvable - lance d'abord audit_genres.py (au moins "
            "une fois) pour produire le cache des genres Spotify par morceau."
        )
    cache_spotify = json.loads(SPOTIFY_GENRE_CACHE_PATH.read_text(encoding="utf-8"))

    # Étape 5 : comparaison des trois signaux, export CSV.
    lignes = []
    accords_surs = []
    nb_avec_isrc = 0
    nb_resolus_deezer = 0

    for track in tracks:
        isrc = cache["isrc_by_track"].get(track["id"])
        if isrc:
            nb_avec_isrc += 1

        tags_actuels_complets = track.get("tags") or []
        tags_genre_actuels = [t for t in tags_actuels_complets if t not in DECADE_TAGS and t not in TAGS_AD_HOC]
        tags_hors_genre = [t for t in tags_actuels_complets if t in DECADE_TAGS or t in TAGS_AD_HOC]

        entree_spotify = cache_spotify.get(track["id"])
        genres_spotify_frais = entree_spotify["genres"] if entree_spotify else []
        suggestion_spotify = suggerer_tags(genres_spotify_frais)

        deezer_info = cache["deezer_track_by_isrc"].get(isrc) if isrc else None
        album_id = deezer_info.get("album_id") if deezer_info and deezer_info.get("found") else None
        deezer_resolu = album_id is not None
        deezer_genres = cache["genres_by_album"].get(str(album_id), []) if deezer_resolu else []
        if deezer_resolu:
            nb_resolus_deezer += 1
        suggestion_deezer = suggerer_tags_deezer(deezer_genres)

        divergent = set(tags_genre_actuels) != set(suggestion_spotify) or (
            deezer_resolu and set(tags_genre_actuels) != set(suggestion_deezer)
        )
        if not divergent:
            continue

        accord = set(suggestion_spotify) == set(suggestion_deezer)
        ligne = {
            "id": track["id"],
            "title": track.get("title", ""),
            "artist": track.get("artist", ""),
            "tags_actuels": "; ".join(tags_actuels_complets),
            "isrc": isrc or "",
            "deezer_genres": "; ".join(deezer_genres),
            "suggestion_spotify": "; ".join(suggestion_spotify),
            "suggestion_deezer": "; ".join(suggestion_deezer),
            "accord_spotify_deezer": "oui" if accord else "non",
            "tags": "; ".join(tags_hors_genre + suggestion_deezer),
        }
        lignes.append(ligne)

        # Corrections les plus sûres : Spotify ET Deezer s'accordent, tous deux contre le tag actuel.
        if accord and deezer_resolu and set(suggestion_spotify) != set(tags_genre_actuels):
            accords_surs.append(ligne)

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    out_path = Path(args.out) if args.out else OUTPUT_DIR / f"genres_deezer_{datetime.now(timezone.utc):%Y%m%d}.csv"

    with out_path.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=CSV_FIELDS)
        writer.writeheader()
        writer.writerows(lignes)

    print(
        f"\nTerminé : {len(tracks)} morceau(x) vérifié(s), {nb_avec_isrc} avec ISRC, "
        f"{nb_resolus_deezer} recoupé(s) sur Deezer, {len(lignes)} écart(s) exporté(s) dans {out_path}.",
        file=sys.stderr,
    )
    print(
        f"\n{len(accords_surs)} correction(s) sûre(s) - Spotify ET Deezer s'accordent contre le tag actuel :",
        file=sys.stderr,
    )
    for ligne in accords_surs:
        print(
            f"  {ligne['id']} - {ligne['artist']} - {ligne['title']} : "
            f"{ligne['tags_actuels'] or '(vide)'} -> {ligne['suggestion_deezer'] or '(vide)'}",
            file=sys.stderr,
        )
    print(
        "\nRelis la colonne 'tags' (pré-remplie avec la suggestion Deezer) au tableur, corrige-la si "
        "besoin, puis réinjecte avec import_tags_csv.py.",
        file=sys.stderr,
    )


if __name__ == "__main__":
    main()
