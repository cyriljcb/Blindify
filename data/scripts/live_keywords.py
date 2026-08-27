"""
Détection de versions live/alternatives (live, acoustique, remix, cover...) à partir du
titre d'une vidéo YouTube — partagé par download_audio.py (évite d'en télécharger de
nouvelles) et audit_live_versions.py (repère celles déjà dans le catalogue).

Retour utilisateur (2026-08-25) : plusieurs morceaux téléchargés étaient en fait des
versions live. La sélection dans download_audio.py se faisait uniquement par proximité de
durée Spotify/YouTube (tolérance ±8s) — une version live coupée dont la durée colle par
coïncidence n'était jamais filtrée par titre.
"""

from __future__ import annotations

import re

MOTS_CLES_SUSPECTS = [
    "live",
    "en direct",
    "acoustic",
    "acoustique",
    "unplugged",
    "concert",
    "session",
    "remix",
    "instrumental",
    "karaoke",
    "karaoké",
    "cover",
    "reprise",
    "tour",
    "festival",
]

_PATTERNS = [(mot, re.compile(rf"\b{re.escape(mot)}\b", re.IGNORECASE)) for mot in MOTS_CLES_SUSPECTS]


def mots_suspects(titre_video: str, titre_officiel: str = "") -> list[str]:
    """Mots-clés suspects présents dans `titre_video` mais absents de `titre_officiel`
    (un morceau officiellement titré "Live and Let Die" n'est pas suspect)."""
    trouves = []
    for mot, pattern in _PATTERNS:
        if pattern.search(titre_video) and not pattern.search(titre_officiel):
            trouves.append(mot)
    return trouves
