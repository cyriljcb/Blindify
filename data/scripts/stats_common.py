"""
Module partagé pour lire `stats.json` v2 (voir docs/architecture.md section 4, "stats.json v2")
et agréger les compteurs de réponse par morceau — utilisé par `report_difficulty.py` et par la
colonne `tauxReussite` de `export_tags_csv.py`, pour ne pas dupliquer la lecture/l'agrégation du
schéma `Reponses`/`Confusions` à deux endroits.

Lecture seule : ne modifie jamais `stats.json` (seul le backend y écrit, voir CLAUDE.md).
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import NamedTuple


class StatsAgregees(NamedTuple):
    n: int  # joueurs exposés (répondants + absents), toutes clés Reponses confondues (round + bonus)
    correct: int
    absent: int
    taux_reussite: float | None  # None si n == 0 (jamais joué, ou jamais répondu)
    taux_absence: float | None
    temps_moyen_bonne_reponse_s: float | None
    taux_par_cible: dict[str, float]  # "Titre"/"Auteur"/"Film"/"Annee" -> taux de réussite


def charger_stats_json(path: Path) -> dict[str, dict]:
    if not path.exists():
        return {}
    return json.loads(path.read_text(encoding="utf-8"))


def _cible_depuis_cle(cle_mode_cible: str) -> str:
    """"Qcm:Titre" -> "Titre" ; "Bonus:TapeReponse:Annee" -> "Annee" — la cible est toujours le
    dernier segment, que la clé porte le préfixe "Bonus:" ou non."""
    return cle_mode_cible.rsplit(":", 1)[-1]


def agreger(entry: dict | None, *, exclure_bonus: bool = False) -> StatsAgregees:
    """`entry` = une valeur de stats.json (ex. `stats.get(trackId)`) — peut être None (morceau
    jamais joué). `exclure_bonus` : ignore les clés préfixées "Bonus:" (réponses données sur audio
    ralenti, donc biaisées pour juger la difficulté réelle du morceau)."""
    reponses = (entry or {}).get("Reponses") or {}

    n = correct = absent = temps_cumul_correct = 0
    par_cible: dict[str, list[int]] = {}  # cible -> [n, correct]

    for cle, valeurs in reponses.items():
        if exclure_bonus and cle.startswith("Bonus:"):
            continue

        cle_n = valeurs.get("N", 0)
        cle_correct = valeurs.get("Correct", 0)
        n += cle_n
        correct += cle_correct
        absent += valeurs.get("Absent", 0)
        temps_cumul_correct += valeurs.get("TempsCorrectCumulMs", 0)

        cible = _cible_depuis_cle(cle)
        bucket = par_cible.setdefault(cible, [0, 0])
        bucket[0] += cle_n
        bucket[1] += cle_correct

    return StatsAgregees(
        n=n,
        correct=correct,
        absent=absent,
        taux_reussite=(correct / n) if n else None,
        taux_absence=(absent / n) if n else None,
        temps_moyen_bonne_reponse_s=(temps_cumul_correct / correct / 1000) if correct else None,
        taux_par_cible={cible: (c / n_c) for cible, (n_c, c) in par_cible.items() if n_c},
    )


def niveau_difficulte(stats: StatsAgregees, *, min_n: int, seuil_facile: float, seuil_difficile: float) -> str:
    if stats.n < min_n or stats.taux_reussite is None:
        return "insuffisant"
    if stats.taux_reussite >= seuil_facile:
        return "facile"
    if stats.taux_reussite <= seuil_difficile:
        return "difficile"
    return "moyen"
