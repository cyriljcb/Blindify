"""
Audit du catalogue à la recherche de tags "genre large" (pop/rock/metal/rap/electro/
rnb-funk-jazz/variete-francaise/latino/monde — voir docs/architecture.md section 3 point 3)
qui ne correspondent plus aux genres Spotify de l'artiste. Complète audit_reissue_years.py
(qui couvre `year`) — retour utilisateur 2026-08-27 : vérifier aussi `genre` pour chaque
morceau, contre Spotify.

Cause possible d'écart : le bucketing initial par mots-clés est faillible sur les cas rares
(retour utilisateur du 2026-08-24 — des morceaux "variété française" tagués "electro" à
tort), et les genres Spotify d'un artiste peuvent avoir changé depuis le premier import.

Pour chaque morceau, re-récupère les artistes + genres Spotify À JOUR (par spotifyId, pas les
`genres` déjà en cache dans tracks.json — potentiellement obsolètes), reproduit le bucketing
par mots-clés, et exporte en CSV les morceaux dont les tags "genre" actuels diffèrent de la
suggestion — à relire manuellement (mêmes règles que export_tags_csv.py/import_tags_csv.py :
ne modifie jamais tracks.json lui-même). Les tags décennie (`annees-*`/`avant-1970`, déjà
couverts par audit_reissue_years.py) et les tags ad hoc (`disney`) sont ignorés ici.

Usage :
    python audit_genres.py [--limit N] [--delay-seconds 0.3]

Résultats mis en cache (output/genre_audit_cache.json), --refresh pour tout re-vérifier.
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

from fetch_spotify_playlist import _api_get, get_access_token

SCRIPTS_DIR = Path(__file__).parent
DATA_DIR = SCRIPTS_DIR.parent
TRACKS_JSON_PATH = DATA_DIR / "tracks.json"
OUTPUT_DIR = SCRIPTS_DIR / "output"
CACHE_PATH = OUTPUT_DIR / "genre_audit_cache.json"
API_BASE = "https://api.spotify.com/v1"
BATCH_SIZE = 50

CSV_FIELDS = ["id", "title", "artist", "tags_actuels", "genres_spotify_frais", "tags"]

DECADE_TAGS = {"avant-1970", "annees-1970", "annees-1980", "annees-1990", "annees-2000", "annees-2010", "annees-2020"}
TAGS_AD_HOC = {"disney"}

# Mots-clés cherchés (sous-chaîne, insensible à la casse) dans les genres Spotify de l'artiste —
# un morceau peut matcher plusieurs buckets à la fois (retour utilisateur : les tags "genre" ne
# sont pas exclusifs, voir architecture.md section 3 point 3). Heuristique, pas une vérité
# absolue : comme le bucketing initial, le résultat reste à relire (colonne "tags_suggeres"),
# jamais réinjecté automatiquement.
BUCKETS: dict[str, list[str]] = {
    "metal": ["metal", "hardcore", "screamo", "grindcore"],
    "rock": ["rock", "punk", "grunge"],
    "pop": ["pop"],
    "electro": ["electro", "house", "techno", "trance", "edm", "dubstep", "drum and bass", "dnb", "synth"],
    "rap": ["rap", "hip hop", "hip-hop", "trap"],
    "rnb-funk-jazz": ["r&b", "rnb", "funk", "jazz", "soul", "motown"],
    # Phrases explicites plutôt qu'un simple "french" — "french touch"/"french house"/"french
    # electro" désignent un mouvement électro français, pas de la chanson (retour utilisateur/
    # docs : piège déjà identifié pour "singer-songwriter"/"folk", même prudence ici).
    "variete-francaise": ["chanson", "variete", "variété", "french pop", "french hip hop", "francoton", "francophone"],
    "latino": ["latin", "reggaeton", "salsa", "bachata", "cumbia", "flamenco"],
    "monde": ["world", "afrobeat", "african", "reggae", "arab", "bollywood"],
}


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


def suggerer_tags(genres: list[str]) -> list[str]:
    genres_normalises = [g.casefold() for g in genres]
    suggestions = []
    for bucket, mots_cles in BUCKETS.items():
        if any(mot in g for g in genres_normalises for mot in mots_cles):
            suggestions.append(bucket)
    return suggestions


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--limit", type=int, default=None, help="Ne traiter que les N premiers morceaux (test)")
    parser.add_argument("--delay-seconds", type=float, default=0.3, help="Pause entre chaque requête Spotify (défaut 0.3s)")
    parser.add_argument("--refresh", action="store_true", help="Ignore le cache, re-vérifie tous les morceaux")
    parser.add_argument("--out", default=None, help="Chemin du CSV en sortie (défaut : data/scripts/output/genres_AAAAMMJJ.csv)")
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

    print("Authentification Spotify...", file=sys.stderr)
    token = get_access_token(client_id, client_secret)

    cache = {} if args.refresh else charger_cache()

    # Étape 1 : artistIds par morceau (batch de 50 spotifyId via GET /tracks).
    a_recuperer = [t for t in tracks if t["id"] not in cache]
    print(f"{len(a_recuperer)} morceau(x) à interroger (sur {len(tracks)}, cache inclus)...", file=sys.stderr)

    for i in range(0, len(a_recuperer), BATCH_SIZE):
        batch = a_recuperer[i : i + BATCH_SIZE]
        print(f"[tracks {i + len(batch)}/{len(a_recuperer)}]", file=sys.stderr)
        data = _api_get(f"{API_BASE}/tracks", token, params={"ids": ",".join(t["spotifyId"] for t in batch)})
        for track, item in zip(batch, data.get("tracks", [])):
            artist_ids = [a["id"] for a in item["artists"]] if item else []
            cache[track["id"]] = {"artistIds": artist_ids, "genres": None}
        sauvegarder_cache(cache)
        time.sleep(args.delay_seconds)

    # Étape 2 : genres par artiste (batch de 50 artistId via GET /artists), un seul appel par
    # artiste même s'il revient sur plusieurs morceaux.
    tous_artist_ids = {aid for entree in cache.values() for aid in entree.get("artistIds", [])}
    genres_par_artiste: dict[str, list[str]] = {}
    artist_ids_list = sorted(tous_artist_ids)
    for i in range(0, len(artist_ids_list), BATCH_SIZE):
        batch = artist_ids_list[i : i + BATCH_SIZE]
        print(f"[artistes {i + len(batch)}/{len(artist_ids_list)}]", file=sys.stderr)
        data = _api_get(f"{API_BASE}/artists", token, params={"ids": ",".join(batch)})
        for artist in data.get("artists", []):
            if artist:
                genres_par_artiste[artist["id"]] = artist.get("genres", [])
        time.sleep(args.delay_seconds)

    for entree in cache.values():
        if entree.get("genres") is None:
            entree["genres"] = sorted({g for aid in entree.get("artistIds", []) for g in genres_par_artiste.get(aid, [])})
    sauvegarder_cache(cache)

    # Étape 3 : comparaison tags actuels (genre large uniquement) vs suggestion fraîche. La
    # colonne "tags" (nom attendu par import_tags_csv.py) reste le nom exact utilisé par
    # export_tags_csv.py : remplace uniquement le sous-ensemble "genre large" par la suggestion,
    # garde décennies/ad hoc inchangés, pour rester réinjectable tel quel après relecture — sans
    # ça, une réinjection directe effacerait les tags décennie (import_tags_csv.py remplace la
    # liste complète, pas seulement les genres).
    lignes_suspectes = []
    for track in tracks:
        entree = cache.get(track["id"])
        if not entree:
            continue

        tags_actuels_complets = track.get("tags") or []
        tags_genre_actuels = [t for t in tags_actuels_complets if t not in DECADE_TAGS and t not in TAGS_AD_HOC]
        tags_hors_genre = [t for t in tags_actuels_complets if t in DECADE_TAGS or t in TAGS_AD_HOC]
        genres_frais = entree["genres"]
        tags_genre_suggeres = suggerer_tags(genres_frais)

        if set(tags_genre_actuels) != set(tags_genre_suggeres):
            lignes_suspectes.append(
                {
                    "id": track["id"],
                    "title": track.get("title", ""),
                    "artist": track.get("artist", ""),
                    "tags_actuels": "; ".join(tags_actuels_complets),
                    "genres_spotify_frais": "; ".join(genres_frais),
                    "tags": "; ".join(tags_hors_genre + tags_genre_suggeres),
                }
            )

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    out_path = Path(args.out) if args.out else OUTPUT_DIR / f"genres_{datetime.now(timezone.utc):%Y%m%d}.csv"

    with out_path.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=CSV_FIELDS)
        writer.writeheader()
        writer.writerows(lignes_suspectes)

    print(
        f"\nTerminé : {len(tracks)} morceau(x) vérifié(s) (cache inclus), "
        f"{len(lignes_suspectes)} écart(s) exporté(s) dans {out_path}.",
        file=sys.stderr,
    )
    print(
        "Relis la colonne 'tags' (pré-remplie avec la suggestion — heuristique par mots-clés, pas "
        "une vérité absolue, voir docs/architecture.md section 3 point 3) au tableur, corrige-la si "
        "besoin, puis réinjecte avec import_tags_csv.py (même colonne 'tags' que "
        "export_tags_csv.py — décennies/disney déjà préservés, pas la peine d'y toucher).",
        file=sys.stderr,
    )


if __name__ == "__main__":
    main()
