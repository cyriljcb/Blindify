"""Lecture/écriture de tracks.json partagée par les scripts (audit 2026-11-10).

Écriture atomique : sérialisation complète dans un fichier temporaire du même dossier, fsync, puis
os.replace. Avant ce module, 6 scripts réécrivaient tracks.json en place (download_audio.py après
chaque morceau, pendant des heures) : un redémarrage du backend au mauvais moment lisait un JSON
tronqué et le backend ne démarrait plus (TracksRepository).

os.replace change l'inode du fichier, exactement comme un `git pull` — le conteneur Docker (bind
mount fichier, lecture seule) relit tracks.json à son redémarrage, ce qui est de toute façon
nécessaire pour qu'il prenne en compte le catalogue modifié.
"""

from __future__ import annotations

import json
import os
import tempfile
from pathlib import Path


def lire_json(path: Path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def ecrire_json_atomique(path: Path, donnees, indent: int | None = 2) -> None:
    path = Path(path)
    texte = json.dumps(donnees, ensure_ascii=False, indent=indent)
    descripteur, temporaire = tempfile.mkstemp(prefix=f".{path.name}.", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(descripteur, "w", encoding="utf-8", newline="\n") as f:
            f.write(texte)
            f.flush()
            os.fsync(f.fileno())
        os.replace(temporaire, path)
    except BaseException:
        if os.path.exists(temporaire):
            os.remove(temporaire)
        raise


# ----- Règles de catalogue partagées (audit 2026-11-10) -----

import re as _re
import unicodedata as _unicodedata

# Noms d'artiste qui contiennent eux-mêmes une virgule : jamais découpés. Le champ `artists` (liste)
# rend cette liste inutile pour les morceaux importés depuis Spotify, qui fournit déjà les artistes
# séparément — elle ne sert qu'à migrer les anciennes entrées (voir corriger_catalogue.py).
NOMS_AVEC_VIRGULE = [
    "Earth, Wind & Fire",
    "Tyler, The Creator",
    "Crosby, Stills, Nash & Young",
    "Crosby, Stills & Nash",
    "Emerson, Lake & Palmer",
    "Peter, Paul and Mary",
    "Blood, Sweat & Tears",
    "Me, Myself and I",
]

_FEATURING = _re.compile(r"\s+(?:featuring|feat\.|ft\.)\s+", _re.IGNORECASE)


def decouper_artistes(artist: str) -> list[str]:
    """Artistes crédités d'un champ `artist` historique ("A, B, C"), en protégeant les noms qui
    contiennent une virgule et en éclatant "A featuring B"."""
    texte = artist or ""
    reserves: dict[str, str] = {}
    for i, nom in enumerate(NOMS_AVEC_VIRGULE):
        motif = _re.compile(_re.escape(nom), _re.IGNORECASE)
        if motif.search(texte):
            jeton = f"\x00{i}\x00"
            reserves[jeton] = motif.search(texte).group(0)
            texte = motif.sub(jeton, texte)
    morceaux = []
    for partie in texte.split(","):
        for sous_partie in _FEATURING.split(partie):
            nom = sous_partie.strip()
            for jeton, original in reserves.items():
                nom = nom.replace(jeton, original)
            if nom and nom not in morceaux:
                morceaux.append(nom)
    return morceaux


def artistes(entry: dict) -> list[str]:
    liste = entry.get("artists") or []
    return [a for a in liste if a] or decouper_artistes(entry.get("artist", ""))


def normaliser_texte(texte: str) -> str:
    texte = (texte or "").lower()
    for source, cible in (("ø", "o"), ("œ", "oe"), ("æ", "ae"), ("ß", "ss"), ("&", " et "), ("+", " et ")):
        texte = texte.replace(source, cible)
    texte = _unicodedata.normalize("NFKD", texte)
    texte = "".join(c for c in texte if not _unicodedata.combining(c))
    texte = _re.sub(r"\(.*?\)|\[.*?\]|\s-\s.*$", " ", texte)  # suffixes de version, "(From ...)"
    texte = _re.sub(r"[^a-z0-9]+", " ", texte).strip()
    return " ".join("et" if mot in ("and", "n") else mot for mot in texte.split())


def cle_doublon(entry: dict) -> tuple[str, str]:
    """Même chanson = même titre normalisé et même premier artiste normalisé."""
    premier = (artistes(entry) or [""])[0]
    return normaliser_texte(entry.get("title", "")), normaliser_texte(premier)


_KARAOKE = _re.compile(
    r"karaok|originally performed|made famous by|in the style of|tribute to|cover version",
    _re.IGNORECASE,
)


def est_version_karaoke(titre: str, artiste: str, album: str | None) -> bool:
    """Karaokés et reprises « à la manière de » importés depuis les playlists Spotify — l'artiste
    crédité est alors un label, la question Auteur n'a plus de sens."""
    return any(_KARAOKE.search(t or "") for t in (titre, artiste, album))
