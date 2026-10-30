"""
Remet d'aplomb la référence humaine des tags de genre large, contre laquelle suggest_tags_llm.py
est mesuré. Constat du 2026-10-09 : le modèle trouve presque toujours la bonne famille (>=1 tag
commun dans 94 % des cas), le désaccord porte sur le second tag, posé de façon irrégulière à la
relecture (181 "pop + rock" contre 131 "rock" seul, sans règle écrite).

Règle de relecture retenue (la même que dans les consignes du modèle) :
    - un genre principal, obligatoire (sauf chanson Disney / comptine) ;
    - un second genre seulement si le morceau aurait CLAIREMENT sa place dans une série de ce
      genre sans détonner ; dans le doute, pas de second genre ;
    - "pop" n'est JAMAIS un second genre.

N'écrit JAMAIS dans tracks.json : les CSV produits se réimportent avec import_tags_csv.py.

Trois commandes :
    python audit_tags_llm.py aveugle [--n 80] [--eval CSV]
        Tire N morceaux où l'humain et le modèle divergent (d'après un CSV de détail de
        suggest_tags_llm.py --evaluer) -> output/audit_aveugle.csv, SANS les tags de l'un ni
        de l'autre. À remplir au tableur : colonnes genrePrincipal / genreSecondaire.
    python audit_tags_llm.py comparer [CSV]
        Confronte la relecture à l'aveugle aux anciens tags humains et à chaque passage du
        modèle (tous les output/eval_tags_llm_*.csv) : qui avait raison ? Écrit aussi
        output/audit_import.csv, réimportable par import_tags_csv.py.
    python audit_tags_llm.py relecture [--eval CSV] [--tous]
        Exporte les morceaux relus dont les tags de genre divergent du dernier passage du
        modèle, avec sa suggestion à côté -> output/relecture_genres.csv (colonne "tags" à
        corriger selon la règle, puis import_tags_csv.py). Les morceaux de l'audit à l'aveugle
        en sont exclus.
"""

from __future__ import annotations

import argparse
import collections
import csv
import random
import sys
from pathlib import Path

from suggest_tags_llm import AUCUN, OUTPUT_DIR, TAG_DISNEY, TAGS_GENRE_LARGE, charger_tracks

AUDIT_PATH = OUTPUT_DIR / "audit_aveugle.csv"
AUDIT_IMPORT_PATH = OUTPUT_DIR / "audit_import.csv"
RELECTURE_PATH = OUTPUT_DIR / "relecture_genres.csv"
GRAINE_AUDIT = 7


def parser_liste(cellule: str) -> list[str]:
    return [v.strip() for v in (cellule or "").split(";") if v.strip()]


def genres_larges(tags: list[str]) -> set[str]:
    return {t for t in tags if t in TAGS_GENRE_LARGE}


def fichiers_eval() -> list[Path]:
    return sorted(OUTPUT_DIR.glob("eval_tags_llm_2*.csv"))


def lire_eval(path: Path) -> dict[str, dict]:
    with path.open(encoding="utf-8-sig", newline="") as f:
        return {row["id"]: row for row in csv.DictReader(f)}


def ecrire_csv(path: Path, lignes: list[dict], champs: list[str]) -> None:
    with path.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=champs)
        writer.writeheader()
        writer.writerows(lignes)


def cmd_aveugle(args) -> None:
    eval_path = Path(args.eval) if args.eval else fichiers_eval()[0]
    if AUDIT_PATH.exists() and not args.ecraser:
        sys.exit(f"{AUDIT_PATH} existe déjà (relecture en cours ?) : --ecraser pour le régénérer.")
    tracks = {t["id"]: t for t in charger_tracks()}
    candidats = []
    for track_id, row in sorted(lire_eval(eval_path).items()):
        humain, modele = parser_liste(row["humain"]), parser_liste(row["modele"])
        # Les chansons Disney sont hors sujet ici (pas de genre large), et la décennie n'est pas auditée.
        if TAG_DISNEY in humain or TAG_DISNEY in modele or track_id not in tracks:
            continue
        if genres_larges(humain) != genres_larges(modele):
            candidats.append(track_id)
    tirage = random.Random(GRAINE_AUDIT).sample(candidats, min(args.n, len(candidats)))
    lignes = [{
        "id": tid, "title": tracks[tid].get("title", ""), "artist": tracks[tid].get("artist", ""),
        "album": tracks[tid].get("album", ""), "year": tracks[tid].get("year", ""),
        "genres": "; ".join(tracks[tid].get("genres") or []),
        "genrePrincipal": "", "genreSecondaire": "",
    } for tid in tirage]
    ecrire_csv(AUDIT_PATH, lignes, list(lignes[0].keys()))
    print(f"{len(lignes)} morceaux (parmi {len(candidats)} désaccords de {eval_path.name}) -> {AUDIT_PATH}")
    print(f"Valeurs possibles : {', '.join(TAGS_GENRE_LARGE)} (genreSecondaire vide = aucun, jamais pop).")


def lire_audit(path: Path) -> dict[str, list[str]]:
    """id -> tags de genre relus à l'aveugle, principal en tête. S'arrête sur une valeur inconnue
    plutôt que de compter une faute de frappe comme un désaccord."""
    reponses, erreurs = {}, []
    with path.open(encoding="utf-8-sig", newline="") as f:
        for row in csv.DictReader(f):
            principal = row["genrePrincipal"].strip().lower()
            secondaire = row["genreSecondaire"].strip().lower()
            if not principal:
                continue  # pas encore relu
            tags = []
            for valeur in (principal, secondaire):
                if valeur in ("", AUCUN):
                    continue
                if valeur not in TAGS_GENRE_LARGE:
                    erreurs.append(f"  {row['id']} ({row['title']}) : valeur inconnue '{valeur}'")
                elif valeur not in tags:
                    tags.append(valeur)
            if secondaire == "pop":
                erreurs.append(f"  {row['id']} ({row['title']}) : 'pop' en second genre (règle : jamais)")
            reponses[row["id"]] = tags
    if erreurs:
        sys.exit("Corrige d'abord le CSV :\n" + "\n".join(erreurs))
    return reponses


def cmd_comparer(args) -> None:
    audit = lire_audit(Path(args.csv) if args.csv else AUDIT_PATH)
    if not audit:
        sys.exit("Aucune ligne remplie dans le CSV d'audit.")
    evals = {p.stem.removeprefix("eval_tags_llm_"): lire_eval(p) for p in fichiers_eval()}
    # Les anciens tags humains viennent du plus ancien passage (tracks.json a pu être réimporté depuis).
    reference = next(iter(evals.values()))

    print(f"=== Audit à l'aveugle : {len(audit)} morceaux relus ===")
    print("Accord avec la relecture à l'aveugle (ensemble exact / même principal / >=1 commun) :")
    sources = {"anciens tags": {tid: parser_liste(r["humain"]) for tid, r in reference.items()}}
    for nom, rows in evals.items():
        sources[f"modèle {nom}"] = {tid: parser_liste(r["modele"]) for tid, r in rows.items()}
    for nom, tags_par_id in sources.items():
        communs = [tid for tid in audit if tid in tags_par_id]
        exact = principal = commun = 0
        for tid in communs:
            nouveau, autre = audit[tid], genres_larges(tags_par_id[tid])
            exact += set(nouveau) == autre
            principal += (nouveau[0] in autre) if nouveau else not autre
            commun += bool(set(nouveau) & autre) or (not nouveau and not autre)
        if communs:
            t = len(communs)
            print(f"  {nom:22} {100 * exact / t:5.1f} %   {100 * principal / t:5.1f} %   "
                  f"{100 * commun / t:5.1f} %   ({t} morceaux)")

    ecarts = collections.Counter()
    for tid, nouveau in audit.items():
        ancien = genres_larges(sources["anciens tags"].get(tid, []))
        if set(nouveau) != ancien:
            ecarts[(" + ".join(sorted(ancien)) or "(aucun)", " + ".join(nouveau) or "(aucun)")] += 1
    print("\nCorrections par rapport aux anciens tags (ancien -> à l'aveugle) :")
    for (avant, apres), c in ecarts.most_common(15):
        print(f"  {c:4}  {avant}  ->  {apres}")

    tracks = {t["id"]: t for t in charger_tracks()}
    lignes = []
    for tid, nouveau in audit.items():
        track = tracks.get(tid)
        if track is None:
            continue
        actuels = track.get("tags") or []
        tags = [t for t in actuels if t not in TAGS_GENRE_LARGE] + nouveau
        lignes.append({"id": tid, "title": track.get("title", ""), "artist": track.get("artist", ""),
                       "tags": "; ".join(tags), "tagsActuels": "; ".join(actuels)})
    ecrire_csv(AUDIT_IMPORT_PATH, lignes, ["id", "title", "artist", "tags", "tagsActuels"])
    print(f"\nTags corrigés prêts à l'import : {AUDIT_IMPORT_PATH}")
    print("  -> import_tags_csv.py output/audit_import.csv --dry-run")


def cmd_relecture(args) -> None:
    eval_path = Path(args.eval) if args.eval else fichiers_eval()[-1]
    detail = lire_eval(eval_path)
    deja_audites = set()
    if AUDIT_PATH.exists():
        with AUDIT_PATH.open(encoding="utf-8-sig", newline="") as f:
            deja_audites = {row["id"] for row in csv.DictReader(f)}

    lignes = []
    for track in charger_tracks():
        tags = track.get("tags") or []
        if not tags or TAG_DISNEY in tags or track["id"] in deja_audites:
            continue
        humain = genres_larges(tags)
        row = detail.get(track["id"])
        modele = genres_larges(parser_liste(row["modele"])) if row else None
        if modele is None:
            if not args.tous:
                continue  # morceau de la réserve d'exemples, jamais passé au modèle en --evaluer
            ecart = "pas de suggestion"
        elif modele == humain:
            if not args.tous:
                continue
            ecart = "accord"
        else:
            # "second tag" : même famille de base, seul le second tag diffère ; "famille" : rien en commun.
            ecart = "second tag" if humain & modele else "famille"
        lignes.append({
            "id": track["id"], "title": track.get("title", ""), "artist": track.get("artist", ""),
            "album": track.get("album", ""), "year": track.get("year", ""),
            "genres": "; ".join(track.get("genres") or []),
            "tags": "; ".join(tags), "tagsActuels": "; ".join(tags),
            "suggestionModele": row["modele"] if row else "", "ecart": ecart,
            "justification": row["justification"] if row else "",
        })
    ordre = {"famille": 0, "second tag": 1, "pas de suggestion": 2, "accord": 3}
    lignes.sort(key=lambda l: (ordre[l["ecart"]], l["tagsActuels"], l["artist"]))
    if not lignes:
        sys.exit("Aucun morceau à relire.")
    ecrire_csv(RELECTURE_PATH, lignes, list(lignes[0].keys()))
    compte = collections.Counter(l["ecart"] for l in lignes)
    print(f"{len(lignes)} morceaux ({', '.join(f'{n} {k}' for k, n in compte.items())}) "
          f"d'après {eval_path.name} -> {RELECTURE_PATH}")
    print("Corrige la colonne 'tags' selon la règle, puis : import_tags_csv.py output/relecture_genres.csv --dry-run")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sous = parser.add_subparsers(dest="commande", required=True)
    p = sous.add_parser("aveugle", help="Tire les désaccords à relire à l'aveugle")
    p.add_argument("--n", type=int, default=80)
    p.add_argument("--eval", help="CSV de détail de --evaluer (défaut : le plus ancien, celui du prompt v3)")
    p.add_argument("--ecraser", action="store_true", help="Régénère le CSV même s'il existe déjà")
    p.set_defaults(func=cmd_aveugle)
    p = sous.add_parser("comparer", help="Compare la relecture à l'aveugle aux anciens tags et au modèle")
    p.add_argument("csv", nargs="?", help=f"CSV d'audit rempli (défaut : {AUDIT_PATH.name})")
    p.set_defaults(func=cmd_comparer)
    p = sous.add_parser("relecture", help="Exporte les désaccords restants pour relecture au tableur")
    p.add_argument("--eval", help="CSV de détail de --evaluer (défaut : le plus récent)")
    p.add_argument("--tous", action="store_true", help="Inclut aussi les accords et les morceaux sans suggestion")
    p.set_defaults(func=cmd_relecture)
    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
