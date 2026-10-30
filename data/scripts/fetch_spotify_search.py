"""
Variante de fetch_spotify_playlist.py pour une liste écrite à la main plutôt qu'une playlist :
lit un CSV `titre;artiste;oeuvre` (ex. film_serie.csv), cherche chaque ligne via l'API Spotify
(/search) et écrit un export JSON au même format, prêt pour download_audio.py.

Pensé pour les tags œuvre (docs/architecture.md section 6, "film-serie") : la colonne `oeuvre`
(nom du film/de la série) est recopiée telle quelle dans le champ `oeuvre` de chaque entrée — c'est
la réponse attendue en cible Film, l'album Spotify n'étant presque jamais la BO (Friends -> "L.P."
des Rembrandts).

Les tags passés via --tags sont les seuls posés : pas de décennie ni de genre large, un morceau
œuvre ne doit compter dans aucune autre catégorie.

Rapport (stderr + output/<csv>_rapport.json) : lignes sans résultat, correspondances douteuses
(titre ou artiste trop éloigné de la demande — à relire avant de lancer download_audio.py), et
morceaux déjà présents dans tracks.json (même id Spotify, ou même titre + premier artiste) :
ceux-là sont écartés de l'export, à retaguer à la main si besoin.

Usage :
    python fetch_spotify_search.py film_serie.csv --tags film-serie [--output FICHIER]
"""

from __future__ import annotations

import argparse
import csv
import json
import os
import re
import sys
import unicodedata
from difflib import SequenceMatcher
from pathlib import Path

from dotenv import load_dotenv

from fetch_spotify_playlist import API_BASE, _api_get, build_track_entries, fetch_artist_genres, get_access_token

SCRIPTS_DIR = Path(__file__).parent
TRACKS_JSON_PATH = SCRIPTS_DIR.parent / "tracks.json"
OUTPUT_DIR = SCRIPTS_DIR / "output"

# En dessous, la correspondance est signalée "douteuse" dans le rapport (elle reste dans l'export).
SEUIL_SIMILARITE = 0.6


def normaliser(texte: str) -> str:
    texte = unicodedata.normalize("NFKD", texte.lower())
    texte = "".join(c for c in texte if not unicodedata.combining(c))
    texte = re.sub(r"\(.*?\)|\[.*?\]|\s-\s.*$", " ", texte)  # suffixes de version, "(From ...)"
    return re.sub(r"[^a-z0-9]+", " ", texte).strip()


def similarite(a: str, b: str) -> float:
    na, nb = normaliser(a), normaliser(b)
    if not na or not nb:
        return 0.0
    if na in nb or nb in na:
        return 1.0
    return SequenceMatcher(None, na, nb).ratio()


# Une reprise/réorchestration matche aussi bien qu'un original sur le titre seul (premier essai :
# Simpson par un quintette de cuivres, Dallas par un orchestre d'harmonie, Scooby-Doo en lofi).
_MOTS_VERSION = re.compile(
    r"\b(arr\.?|arranged|mix|remix|lofi|lo-fi|cover|version|extended|orchestra|quintet|"
    r"piano|instrumental|karaoke|tribute|live)\b",
    re.IGNORECASE,
)


def penalite_version(track: dict, titre: str, artiste: str) -> float:
    """Pénalise les versions alternatives et les artistes absents de la demande."""
    nom = track["name"] + " " + " ".join(a["name"] for a in track["artists"])
    demandes = {m.lower() for m in _MOTS_VERSION.findall(titre + " " + artiste)}
    penalite = 0.4 * len({m.lower() for m in _MOTS_VERSION.findall(nom)} - demandes)
    artistes_en_trop = [a for a in track["artists"] if similarite(artiste, a["name"]) < SEUIL_SIMILARITE]
    return penalite + 0.1 * len(artistes_en_trop)


def rechercher(token: str, titre: str, artiste: str) -> tuple[dict | None, float, float]:
    """Meilleur candidat Spotify (titre, puis artiste, puis popularité) et ses deux scores."""
    meilleur, score_meilleur = None, (-1.0, -1.0, -1)
    for requete in (f'track:"{titre}" artist:"{artiste}"', f"{titre} {artiste}", titre):
        data = _api_get(f"{API_BASE}/search", token, params={"q": requete, "type": "track", "limit": 10, "market": "FR"})
        for track in data.get("tracks", {}).get("items", []):
            s_titre = similarite(titre, track["name"])
            s_artiste = max((similarite(artiste, a["name"]) for a in track["artists"]), default=0.0)
            score = (round(s_titre + s_artiste - penalite_version(track, titre, artiste), 2), s_titre,
                     track.get("popularity", 0))
            if score > score_meilleur:
                meilleur, score_meilleur = track, score
        if score_meilleur[0] >= 2 * SEUIL_SIMILARITE + 0.5:
            break  # correspondance franche, inutile d'élargir la requête
    if meilleur is None:
        return None, 0.0, 0.0
    s_artiste = round(score_meilleur[0] - score_meilleur[1], 2)
    return meilleur, round(score_meilleur[1], 2), s_artiste


def cle_titre_artiste(titre: str, artiste: str) -> tuple[str, str]:
    return normaliser(titre), normaliser(artiste.split(",")[0])


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("csv", help="CSV titre;artiste;oeuvre")
    parser.add_argument("--tags", required=True, help="Tags posés sur tous les morceaux, séparés par des virgules")
    parser.add_argument("--output", default=None, help="Export JSON (défaut : output/<csv>.json)")
    args = parser.parse_args()

    load_dotenv(SCRIPTS_DIR / ".env")
    client_id = os.environ.get("SPOTIFY_CLIENT_ID")
    client_secret = os.environ.get("SPOTIFY_CLIENT_SECRET")
    if not client_id or not client_secret:
        sys.exit("SPOTIFY_CLIENT_ID / SPOTIFY_CLIENT_SECRET manquants dans data/scripts/.env")

    tags = [t.strip() for t in args.tags.split(",") if t.strip()]
    csv_path = Path(args.csv)
    with csv_path.open("r", encoding="utf-8-sig", newline="") as f:
        lignes = list(csv.DictReader(f, delimiter=";"))

    catalogue = json.loads(TRACKS_JSON_PATH.read_text(encoding="utf-8")) if TRACKS_JSON_PATH.exists() else []
    par_spotify_id = {t.get("spotifyId") or t["id"]: t for t in catalogue}
    par_titre_artiste = {cle_titre_artiste(t["title"], t["artist"]): t for t in catalogue}

    token = get_access_token(client_id, client_secret)
    trouves: list[tuple[dict, dict]] = []
    rapport = {"introuvables": [], "douteux": [], "dejaPresents": []}
    ids_vus: set[str] = set()

    for i, ligne in enumerate(lignes, start=1):
        titre, artiste, oeuvre = ligne["titre"].strip(), ligne["artiste"].strip(), ligne["oeuvre"].strip()
        track, s_titre, s_artiste = rechercher(token, titre, artiste)
        if track is None:
            print(f"[{i}/{len(lignes)}] INTROUVABLE {artiste} - {titre}", file=sys.stderr)
            rapport["introuvables"].append(ligne)
            continue

        artistes_trouves = ", ".join(a["name"] for a in track["artists"])
        resume = {**ligne, "trouve": f"{artistes_trouves} - {track['name']}", "album": track["album"]["name"],
                  "spotifyId": track["id"], "scoreTitre": s_titre, "scoreArtiste": s_artiste}
        print(f"[{i}/{len(lignes)}] {artiste} - {titre} -> {resume['trouve']} ({s_titre}/{s_artiste})", file=sys.stderr)

        existant = par_spotify_id.get(track["id"]) or par_titre_artiste.get(cle_titre_artiste(track["name"], artistes_trouves))
        if existant is not None:
            rapport["dejaPresents"].append({**resume, "idCatalogue": existant["id"], "tagsCatalogue": existant.get("tags", [])})
            continue
        if track["id"] in ids_vus:
            continue
        ids_vus.add(track["id"])

        if s_titre < SEUIL_SIMILARITE or s_artiste < SEUIL_SIMILARITE:
            rapport["douteux"].append(resume)
        trouves.append((track, ligne))

    artist_ids = [a["id"] for track, _ in trouves for a in track["artists"]]
    genres_by_artist = fetch_artist_genres(token, artist_ids)
    entries = build_track_entries([track for track, _ in trouves], genres_by_artist, tags)
    for entry, (_, ligne) in zip(entries, trouves):
        entry["oeuvre"] = ligne["oeuvre"].strip()

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    export_path = Path(args.output) if args.output else OUTPUT_DIR / f"{csv_path.stem}.json"
    export_path.write_text(json.dumps(entries, ensure_ascii=False, indent=2), encoding="utf-8")
    rapport_path = OUTPUT_DIR / f"{csv_path.stem}_rapport.json"
    rapport_path.write_text(json.dumps(rapport, ensure_ascii=False, indent=2), encoding="utf-8")

    print(
        f"\n{len(entries)} morceaux exportés dans {export_path} — {len(rapport['introuvables'])} introuvables, "
        f"{len(rapport['douteux'])} douteux, {len(rapport['dejaPresents'])} déjà dans tracks.json "
        f"(détail : {rapport_path}).",
        file=sys.stderr,
    )


if __name__ == "__main__":
    main()
