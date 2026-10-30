"""
Suggère les tags (décennie + genre large + disney) et, à titre d'essai, des valeurs pour le
champ genres, à l'aide d'un modèle de langage local servi par Ollama sur le PC (jamais sur le
Pi). Validé le 2026-10-08 : les 17 tags existants sont conservés tels quels, aucun nouveau tag.

N'écrit JAMAIS dans tracks.json — même principe que export_tags_csv.py / suggest_genres_lastfm.py :
le script propose un CSV, l'organisateur tranche au tableur, import_tags_csv.py réinjecte (seule
la colonne "tags" est relue, les autres colonnes sont là pour juger).

Pourquoi le modèle propose aussi la décennie (au lieu de la déduire de `year`) : sur les
morceaux relus à la main, ~13 % portent une décennie différente de celle de `year`, parce que
Spotify date souvent une compilation/réédition (ex. Les lacs du Connemara, year=2004, relu
"annees-1980"). La décennie "brute" déduite de `year` reste affichée dans la justification
quand le modèle s'en écarte, pour arbitrer au tableur.

Sortie contrainte par schéma JSON (format Ollama) : le modèle ne peut répondre qu'avec des tags
de la liste fermée, et des genres de la whitelist du catalogue (valeurs présentes au moins
--min-count fois, même règle que suggest_genres_lastfm.py).

Exemples fournis au modèle : un échantillon fixe de morceaux déjà relus (pas de règles écrites
à la main) — exclus de la mesure en mode --evaluer.

Deux modes :
    python suggest_tags_llm.py [--limit N] [--out CHEMIN]
        Morceaux sans tags -> CSV de suggestions, trié des moins sûrs aux plus sûrs.
    python suggest_tags_llm.py --evaluer [--limit N]
        Morceaux déjà relus -> compare aux tags humains, imprime un rapport de fiabilité.
        Les genres d'entrée sont masqués sur la moitié des morceaux (choix déterministe par
        id), pour mesurer à la fois les tags avec/sans genres Spotify et la suggestion de genres.

Reprise après coupure : chaque réponse est ajoutée à output/llm_cache.jsonl (clé = id +
modèle + version du prompt + genres masqués ou non) — une relance ne refait que ce qui manque.

Nécessite Ollama lancé localement avec le modèle téléchargé (ollama pull qwen3:14b).
"""

from __future__ import annotations

import argparse
import collections
import csv
import hashlib
import json
import random
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

import requests

SCRIPTS_DIR = Path(__file__).parent
DATA_DIR = SCRIPTS_DIR.parent
TRACKS_JSON_PATH = DATA_DIR / "tracks.json"
OUTPUT_DIR = SCRIPTS_DIR / "output"
CACHE_PATH = OUTPUT_DIR / "llm_cache.jsonl"

OLLAMA_URL = "http://localhost:11434/api/chat"
MODELE_DEFAUT = "qwen3:14b"

# À incrémenter dès que le prompt ou le schéma change : invalide le cache.
PROMPT_VERSION = 6

TAGS_DECENNIE = ["avant-1970", "annees-1970", "annees-1980", "annees-1990", "annees-2000", "annees-2010", "annees-2020"]
# Liste revue le 2026-10-25 (architecture.md section 11) : eurodance ajouté, rnb-funk-jazz découpé en
# funk-disco / rnb-soul, metal fondu dans rock. L'ordre départage principal/secondaire des tags relus.
TAGS_GENRE_LARGE = ["pop", "rock", "rap", "eurodance", "electro", "funk-disco", "rnb-soul", "variete-francaise",
                    "latino", "monde"]
TAG_DISNEY = "disney"
AUCUN = "aucun"

NB_EXEMPLES_DEFAUT = 30
GRAINE_EXEMPLES = 42

CSV_FIELDS = ["id", "title", "artist", "album", "year", "genres", "genresSuggeres", "tags", "tagsActuels",
              "confiance", "justification"]

CONSIGNES = """Tu classes des morceaux pour un blindtest familial (public francophone et néerlandophone).
Réponds en JSON avec, dans cet ordre :
- "justification" : une phrase courte en français sur ce que tu sais du morceau (style, époque,
  langue). Écris-la AVANT de choisir les tags.
- "anneeOriginale" : l'année de SORTIE ORIGINALE du morceau. L'année fournie vient de Spotify et
  correspond parfois à une compilation ou une réédition plus tardive : si tu SAIS que le morceau
  est sorti plus tôt, donne la vraie année. Sinon, recopie l'année fournie telle quelle.
- "genrePrincipal" : LE genre large qui décrit le mieux le morceau, parmi la liste fermée
  ("aucun" seulement si aucun ne convient, ex. chanson Disney ou comptine).
- "genreSecondaire" : un second genre large, ou "aucun". Les tags servent à composer des séries
  thématiques : n'ajoute un second genre que si le morceau aurait CLAIREMENT sa place dans une
  série de ce genre sans détonner (ex. un tube électro-pop de David Guetta : principal pop,
  secondaire electro ; un tube pop-rock comme "What Makes You Beautiful" : principal pop,
  secondaire rock ; une chanson pop chantée en français : principal pop, secondaire
  variete-francaise ; une chanson française à texte ou de variété classique, sans couleur
  pop marquée : variete-francaise seul). Dans le
  doute, "aucun". "pop" n'est JAMAIS un genre secondaire : soit le morceau est d'abord pop
  (principal), soit il appartient d'abord à une autre famille et "pop" ne s'ajoute pas.
  "variete-francaise" = chanson/variété chantée en français. C'est la langue CHANTÉE qui compte,
  pas la nationalité ni le nom de l'artiste : une artiste québécoise ou française qui chante en
  anglais n'est pas de la variété française. Les morceaux néerlandais non plus. "pop" inclut
  la synthpop et l'électro-pop chantée : une chanteuse ou un chanteur pop avec des synthés
  reste "pop". "electro" = musique de club/DJ
  (house, french touch, EDM, trance), l'artiste est souvent un DJ ou un producteur.
  "eurodance" = les tubes dance des années 80 et 90, ceux qui font bouger une piste de danse
  avec un son électronique ou synthétique : dance 90 (Corona, Ace of Base, 2 Unlimited,
  Haddaway), euro-disco et synthpop dansante des années 80 (Modern Talking, a-ha, Kim Wilde,
  "Voyage voyage", "Maniac"), hits dance de la fin des années 90/début 2000 (Eiffel 65,
  Gigi D'Agostino, O-Zone). N'hésite pas à le poser dès qu'un titre de cette époque est fait
  pour danser ; "electro", "pop" ou "funk-disco" peuvent alors s'y ajouter selon le son. "funk-disco" = disco, funk, hi-NRG. "rnb-soul" =
  R&B, soul, Motown, jazz et blues. "rock" inclut hard rock, punk et metal. Un morceau
  de rap reste "rap" seul : n'ajoute "rnb-soul" que si le refrain chanté est franchement R&B.
  De la musique de club pure (DJ sans structure de chanson pop) reste "electro" seul.
  "monde" = reggae, dancehall, afro, zouk, musiques du monde. "latino" = chanté en espagnol/
  portugais ou rythmes latins.
- "disney" : true seulement pour une chanson issue d'un film/série Disney ou Pixar, y compris
  sa version française (ex. "Le rapport du matin" du Roi Lion, "La Belle et la Bête" chantée
  en français) : le titre français ou un album de BO/compilation Disney sont de bons indices.
  Une chanson Disney ne porte JAMAIS de genre large : genrePrincipal et genreSecondaire "aucun".
- "genres" : 1 à 4 genres fins pris dans la liste fournie, les plus caractéristiques du morceau
  (s'ils sont fournis par Spotify, garde les plus pertinents). Liste vide seulement si tu ne
  sais vraiment rien du style.
- "confiance" : entre 0 et 1, ta certitude sur les tags. Un morceau que tu ne connais pas,
  classé uniquement d'après l'artiste ou l'album, doit avoir une confiance de 0.5 ou moins.
Appuie-toi d'abord sur les métadonnées (artiste, album, genres Spotify) plutôt que sur ta
mémoire pour les morceaux obscurs. Les exemples ci-dessous ont été classés à la main par
l'organisateur : imite ses choix de tags (la décennie indiquée est celle de la sortie originale)."""


def charger_tracks() -> list[dict]:
    if not TRACKS_JSON_PATH.exists():
        sys.exit(f"{TRACKS_JSON_PATH} introuvable.")
    return json.loads(TRACKS_JSON_PATH.read_text(encoding="utf-8"))


def decennie_depuis_annee(year: int | None) -> str | None:
    if not year:
        return None
    if year < 1970:
        return "avant-1970"
    return f"annees-{min(year // 10 * 10, 2020)}"


def construire_whitelist_genres(tracks: list[dict], min_count: int) -> list[str]:
    compteur = collections.Counter(g for t in tracks for g in t.get("genres") or [])
    return sorted(g for g, n in compteur.items() if n >= min_count)


def decouper_tags_humains(tags: list[str]) -> tuple[str | None, set[str], bool]:
    decennies = [t for t in tags if t in TAGS_DECENNIE]
    return (decennies[0] if decennies else None,
            {t for t in tags if t in TAGS_GENRE_LARGE},
            TAG_DISNEY in tags)


def genres_masques(track_id: str) -> bool:
    """Choix déterministe (stable d'une relance à l'autre) de la moitié des morceaux dont on
    cache les genres Spotify en mode --evaluer."""
    return hashlib.md5(track_id.encode("utf-8")).digest()[0] % 2 == 0


def dans_reserve_exemples(track_id: str) -> bool:
    """Un quart des morceaux relus (choix déterministe par id) forme la réserve où sont pris
    les exemples ; les trois autres quarts servent de jeu de test en mode --evaluer. Séparation
    fixe, pour que les réglages (--exemples, --reflexion) soient comparés sur les mêmes morceaux."""
    return hashlib.md5(track_id.encode("utf-8")).digest()[1] % 4 == 0


def choisir_exemples(relus: list[dict], nb_exemples: int) -> list[dict]:
    """Échantillon fixe et varié : au moins un exemple par genre large et par décennie, au
    moins quelques cas où la décennie relue diffère de `year` (le cas réédition), puis du
    hasard à graine fixe pour compléter."""
    rng = random.Random(GRAINE_EXEMPLES)
    pool = sorted((t for t in relus if dans_reserve_exemples(t["id"])), key=lambda t: t["id"])
    rng.shuffle(pool)
    choisis: dict[str, dict] = {}

    def ajouter_premier(predicat) -> None:
        for t in pool:
            if t["id"] not in choisis and predicat(t):
                choisis[t["id"]] = t
                return

    # Les exemples avec genres Spotify servent aussi à montrer quels genres fins garder.
    pool.sort(key=lambda t: not t.get("genres"))
    for tag in TAGS_GENRE_LARGE + TAGS_DECENNIE + [TAG_DISNEY]:
        ajouter_premier(lambda t, tag=tag: tag in t["tags"])
    for _ in range(4):
        ajouter_premier(lambda t: decouper_tags_humains(t["tags"])[0] != decennie_depuis_annee(t.get("year")))
    for t in pool:
        if len(choisis) >= nb_exemples:
            break
        choisis.setdefault(t["id"], t)
    return list(choisis.values())


def description_morceau(track: dict, masquer_genres: bool) -> str:
    genres = [] if masquer_genres else (track.get("genres") or [])
    return json.dumps({
        "titre": track.get("title", ""),
        "artiste": track.get("artist", ""),
        "album": track.get("album", ""),
        "annee": track.get("year"),
        "genresSpotify": genres,
    }, ensure_ascii=False)


def principal_secondaire(genre_large: set[str]) -> tuple[str, str]:
    """Les tags relus forment un ensemble sans ordre : "pop" est principal quand il est présent
    (règle : jamais secondaire), sinon l'ordre de TAGS_GENRE_LARGE départage (eurodance avant
    electro, rap avant rnb-soul...)."""
    ordonnes = sorted(genre_large, key=TAGS_GENRE_LARGE.index)
    return (ordonnes[0] if ordonnes else AUCUN), (ordonnes[1] if len(ordonnes) > 1 else AUCUN)


def ligne_exemple(track: dict, whitelist: set[str]) -> str:
    decennie, genre_large, disney = decouper_tags_humains(track["tags"])
    principal, secondaire = principal_secondaire(genre_large)
    genres = [g for g in track.get("genres") or [] if g in whitelist][:4]
    return (f"- {description_morceau(track, masquer_genres=False)} => décennie {decennie}, "
            f"genrePrincipal {principal}, genreSecondaire {secondaire}, disney {str(disney).lower()}, "
            f"genres {genres}")


def construire_messages(exemples: list[dict], whitelist: list[str], track: dict, masquer_genres: bool) -> list[dict]:
    """Exemples regroupés dans le message système (et non en faux tours de dialogue) : sinon
    le modèle recopie la confiance/justification des exemples au lieu de les produire. Le
    message système est identique d'un morceau à l'autre, ce qui laisse Ollama réutiliser
    son cache de prompt."""
    whitelist_set = set(whitelist)
    systeme = (CONSIGNES
               + "\n\nTags de genre large autorisés : " + ", ".join(TAGS_GENRE_LARGE)
               + "\nGenres fins autorisés : " + ", ".join(whitelist)
               + "\n\nExemples classés par l'organisateur :\n"
               + "\n".join(ligne_exemple(ex, whitelist_set) for ex in exemples))
    return [{"role": "system", "content": systeme},
            {"role": "user", "content": description_morceau(track, masquer_genres)}]


def schema_reponse(whitelist: list[str]) -> dict:
    return {
        "type": "object",
        "properties": {
            "justification": {"type": "string"},
            "anneeOriginale": {"type": "integer"},
            "genrePrincipal": {"type": "string", "enum": TAGS_GENRE_LARGE + [AUCUN]},
            "genreSecondaire": {"type": "string", "enum": TAGS_GENRE_LARGE + [AUCUN]},
            "disney": {"type": "boolean"},
            "genres": {"type": "array", "items": {"type": "string", "enum": whitelist}, "maxItems": 4},
            "confiance": {"type": "number"},
        },
        "required": ["justification", "anneeOriginale", "genrePrincipal", "genreSecondaire", "disney", "genres",
                     "confiance"],
    }


def interroger(modele: str, messages: list[dict], schema: dict, reflexion: bool) -> dict:
    r = requests.post(OLLAMA_URL, timeout=300, json={
        "model": modele,
        "messages": messages,
        "format": schema,
        "stream": False,
        # Avec reflexion, le modèle raisonne avant de répondre : plus lent, parfois plus juste.
        "think": reflexion,
        "options": {"temperature": 0, "num_ctx": 16384},
    })
    r.raise_for_status()
    return json.loads(r.json()["message"]["content"])


def nettoyer(brut: dict, whitelist: set[str], track: dict) -> dict:
    """Garde-fou en plus du schéma : dédoublonne et retire toute valeur hors liste. La décennie
    est déduite par le code de l'année originale proposée (un modèle se trompe moins sur une
    année que sur une étiquette de décennie) ; une année "corrigée" plus RÉCENTE que celle de
    Spotify est ignorée (une réédition est toujours postérieure à l'original). Un "pop" proposé
    en secondaire est retiré (règle de relecture : pop n'est jamais un second tag)."""
    principal = brut.get("genrePrincipal")
    secondaire = brut.get("genreSecondaire")
    tags_genre = [principal] if principal in TAGS_GENRE_LARGE else []
    if secondaire in TAGS_GENRE_LARGE and secondaire != "pop" and secondaire not in tags_genre:
        tags_genre.append(secondaire)
    annee = brut.get("anneeOriginale")
    annee_spotify = track.get("year")
    if not isinstance(annee, int) or annee < 1900 or (annee_spotify and annee > annee_spotify):
        annee = annee_spotify
    return {
        "anneeOriginale": annee,
        "decennie": decennie_depuis_annee(annee),
        # Une chanson Disney ne porte pas de genre large (107 sur 110 dans le catalogue relu),
        # même si le modèle en propose un.
        # Ordre conservé : le premier élément est le genre principal (utilisé par --evaluer).
        "tagsGenre": [] if brut.get("disney") else tags_genre,
        "disney": bool(brut.get("disney")),
        "genres": list(dict.fromkeys(g for g in brut.get("genres") or [] if g in whitelist)),
        "confiance": max(0.0, min(1.0, float(brut.get("confiance") or 0))),
        "justification": str(brut.get("justification") or "").strip(),
    }


def charger_cache() -> dict[str, dict]:
    if not CACHE_PATH.exists():
        return {}
    cache = {}
    for ligne in CACHE_PATH.read_text(encoding="utf-8").splitlines():
        if ligne.strip():
            entree = json.loads(ligne)
            cache[entree["cle"]] = entree["reponse"]
    return cache


def cle_cache(track_id: str, reglages: str, masquer: bool) -> str:
    return f"{track_id}|{reglages}|v{PROMPT_VERSION}|{'masque' if masquer else 'genres'}"


def traiter(tracks: list[dict], exemples: list[dict], whitelist: list[str], modele: str, reflexion: bool,
            masquer_pour) -> dict[str, dict]:
    """Interroge le modèle pour chaque morceau (ou relit le cache). `masquer_pour(track)` dit
    si les genres Spotify sont cachés au modèle pour ce morceau."""
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    cache = charger_cache()
    schema = schema_reponse(whitelist)
    whitelist_set = set(whitelist)
    resultats: dict[str, dict] = {}
    debut = time.monotonic()
    nb_appels = 0

    with CACHE_PATH.open("a", encoding="utf-8") as f_cache:
        for i, track in enumerate(tracks, 1):
            masquer = masquer_pour(track)
            cle = cle_cache(track["id"], f"{modele}|ex{len(exemples)}|{'think' if reflexion else 'direct'}", masquer)
            if cle not in cache:
                try:
                    brut = interroger(modele, construire_messages(exemples, whitelist, track, masquer), schema, reflexion)
                except (requests.RequestException, ValueError, KeyError) as e:
                    print(f"  [{i}/{len(tracks)}] échec {track.get('artist')} - {track.get('title')} : {e}", file=sys.stderr)
                    continue
                cache[cle] = brut
                f_cache.write(json.dumps({"cle": cle, "reponse": brut}, ensure_ascii=False) + "\n")
                f_cache.flush()
                nb_appels += 1
            resultats[track["id"]] = nettoyer(cache[cle], whitelist_set, track)
            if i % 25 == 0 or i == len(tracks):
                ecoule = time.monotonic() - debut
                rythme = f", {ecoule / nb_appels:.1f} s/morceau" if nb_appels else ""
                print(f"[{i}/{len(tracks)}] {nb_appels} appel(s) au modèle{rythme}", file=sys.stderr)
    return resultats


def tags_suggeres(reponse: dict, track: dict) -> list[str]:
    decennie = reponse["decennie"] or decennie_depuis_annee(track.get("year"))
    tags = [decennie] if decennie else []
    if reponse["disney"]:
        tags.append(TAG_DISNEY)
    tags += [t for t in TAGS_GENRE_LARGE if t in reponse["tagsGenre"]]
    return tags


def mode_suggestion(tracks: list[dict], exemples: list[dict], whitelist: list[str], modele: str, reflexion: bool,
                    limit: int | None, out: str | None) -> None:
    cibles = [t for t in tracks if not t.get("tags")]
    if limit:
        cibles = cibles[:limit]
    if not cibles:
        sys.exit("Aucun morceau sans tag.")
    print(f"{len(cibles)} morceau(x) sans tag à classer avec {modele}.", file=sys.stderr)

    resultats = traiter(cibles, exemples, whitelist, modele, reflexion, masquer_pour=lambda t: False)

    lignes = []
    for track in cibles:
        reponse = resultats.get(track["id"])
        if reponse is None:
            continue
        justification = reponse["justification"]
        decennie_annee = decennie_depuis_annee(track.get("year"))
        if reponse["decennie"] != decennie_annee:
            justification = f"[année corrigée {track.get('year')} -> {reponse['anneeOriginale']}] {justification}"
        lignes.append({
            "id": track["id"],
            "title": track.get("title", ""),
            "artist": track.get("artist", ""),
            "album": track.get("album", ""),
            "year": track.get("year", ""),
            "genres": "; ".join(track.get("genres") or []),
            # Seulement pour les morceaux sans genres : les genres déjà présents ont été posés
            # par sync/Last.fm ou relus, on ne les remet pas en cause ici.
            "genresSuggeres": "" if track.get("genres") else "; ".join(reponse["genres"]),
            "tags": "; ".join(tags_suggeres(reponse, track)),
            "tagsActuels": "",
            "confiance": round(reponse["confiance"], 2),
            "justification": justification,
        })
    lignes.sort(key=lambda l: l["confiance"])

    out_path = Path(out) if out else OUTPUT_DIR / f"tags_llm_{datetime.now(timezone.utc):%Y%m%d}.csv"
    with out_path.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=CSV_FIELDS)
        writer.writeheader()
        writer.writerows(lignes)
    print(f"{len(lignes)} suggestion(s) écrites dans {out_path} (moins sûres en tête).", file=sys.stderr)
    print("Relis la colonne 'tags' au tableur, puis : import_tags_csv.py CHEMIN.csv --dry-run", file=sys.stderr)


def pct(n: int, total: int) -> str:
    return f"{100 * n / total:5.1f} % ({n}/{total})" if total else "  n/a"


def mode_evaluation(relus: list[dict], exemples: list[dict], whitelist: list[str], modele: str, reflexion: bool,
                    limit: int | None) -> None:
    cibles = sorted((t for t in relus if not dans_reserve_exemples(t["id"])), key=lambda t: t["id"])
    if limit:
        # Échantillon aléatoire à graine fixe plutôt que les N premiers ids, pour rester
        # représentatif même sur un petit --limit.
        cibles = random.Random(GRAINE_EXEMPLES + 1).sample(cibles, min(limit, len(cibles)))
    print(f"Évaluation sur {len(cibles)} morceau(x) relus ({len(exemples)} autres servent d'exemples).", file=sys.stderr)

    resultats = traiter(cibles, exemples, whitelist, modele, reflexion, masquer_pour=lambda t: genres_masques(t["id"]))

    whitelist_set = set(whitelist)
    n = ok_decennie = ok_decennie_annee = ok_genre_exact = ok_genre_commun = ok_hors_pop = 0
    ok_principal = n_secondaire = ok_secondaire = 0
    nb_tags_genre = collections.Counter()  # (nb relus, nb modèle) -> morceaux
    par_tag = {t: collections.Counter() for t in TAGS_GENRE_LARGE + [TAG_DISNEY]}
    confusions = collections.Counter()
    par_condition = {True: [0, 0], False: [0, 0]}  # masqué -> [n, exact]
    genres_vp = genres_suggeres = genres_attendus = 0
    seaux_confiance = collections.defaultdict(lambda: [0, 0])
    detail = []

    for track in cibles:
        reponse = resultats.get(track["id"])
        if reponse is None:
            continue
        n += 1
        decennie_h, genre_h, disney_h = decouper_tags_humains(track["tags"])
        genre_m = set(reponse["tagsGenre"])
        masque = genres_masques(track["id"])

        ok_decennie += reponse["decennie"] == decennie_h
        ok_decennie_annee += decennie_depuis_annee(track.get("year")) == decennie_h
        exact = genre_m == genre_h
        ok_genre_exact += exact
        # "pop" en second tag est posé de façon peu systématique à la relecture (181 "pop + rock"
        # contre 131 "rock" seul) : mesure à part, sans lui, de la famille musicale elle-même.
        ok_hors_pop += (genre_m - {"pop"}) == (genre_h - {"pop"})
        ok_genre_commun += bool(genre_m & genre_h) or (not genre_m and not genre_h)
        # Le premier tag du modèle est son genre principal : juste s'il fait partie des tags relus.
        ok_principal += (reponse["tagsGenre"][0] in genre_h) if reponse["tagsGenre"] else not genre_h
        if len(reponse["tagsGenre"]) > 1:
            n_secondaire += 1
            ok_secondaire += reponse["tagsGenre"][1] in genre_h
        nb_tags_genre[(len(genre_h), len(genre_m))] += 1
        par_condition[masque][0] += 1
        par_condition[masque][1] += exact
        seau = min(int(reponse["confiance"] * 10), 9) / 10
        seaux_confiance[seau][0] += 1
        seaux_confiance[seau][1] += exact

        for tag, humain, modele_dit in [(t, t in genre_h, t in genre_m) for t in TAGS_GENRE_LARGE] + \
                                       [(TAG_DISNEY, disney_h, reponse["disney"])]:
            par_tag[tag]["vp" if humain and modele_dit else "fn" if humain else "fp" if modele_dit else "vn"] += 1
        if not exact:
            confusions[(" + ".join(sorted(genre_h)) or "(aucun)", " + ".join(sorted(genre_m)) or "(aucun)")] += 1

        if masque:
            attendus = {g for g in track.get("genres") or [] if g in whitelist_set}
            if attendus:
                genres_attendus += len(attendus)
                genres_suggeres += len(reponse["genres"])
                genres_vp += len(attendus & set(reponse["genres"]))

        detail.append({
            "id": track["id"], "artist": track.get("artist", ""), "title": track.get("title", ""),
            "year": track.get("year", ""), "genresMasques": masque,
            "humain": "; ".join(track["tags"]), "modele": "; ".join(tags_suggeres(reponse, track)),
            "genresHumain": "; ".join(track.get("genres") or []), "genresModele": "; ".join(reponse["genres"]),
            "decennieOk": reponse["decennie"] == decennie_h, "genreLargeOk": exact,
            "confiance": round(reponse["confiance"], 2), "justification": reponse["justification"],
        })

    print(f"\n=== Fiabilité de {modele} sur {n} morceaux relus ===")
    print(f"Décennie              : {pct(ok_decennie, n)}   (référence year brute : {pct(ok_decennie_annee, n)})")
    print(f"Genre large hors pop  : {pct(ok_hors_pop, n)}")
    print(f"Genre large exact     : {pct(ok_genre_exact, n)}")
    print(f"  avec genres Spotify : {pct(par_condition[False][1], par_condition[False][0])}")
    print(f"  sans genres Spotify : {pct(par_condition[True][1], par_condition[True][0])}")
    print(f"Genre large >=1 commun : {pct(ok_genre_commun, n)}")
    print(f"Genre principal juste : {pct(ok_principal, n)}   (principal du modèle parmi les tags relus)")
    print(f"Second tag juste      : {pct(ok_secondaire, n_secondaire)}   (quand le modèle en pose un)")
    print("\nPar tag (précision = quand le modèle le pose, il a raison ; rappel = il le trouve quand il faut) :")
    for tag, c in par_tag.items():
        precision = pct(c["vp"], c["vp"] + c["fp"])
        rappel = pct(c["vp"], c["vp"] + c["fn"])
        print(f"  {tag:18} précision {precision:22} rappel {rappel}")
    print("\nGenre large exact selon la confiance annoncée :")
    for seau in sorted(seaux_confiance):
        total, bons = seaux_confiance[seau]
        print(f"  {seau:.1f}-{seau + 0.1:.1f} : {pct(bons, total)}")
    print("\nNombre de tags de genre (lignes : relus, colonnes : modèle) :")
    print("        " + "".join(f"{m:>7}" for m in range(3)))
    for h in range(4):
        if any(nb_tags_genre[(h, m)] for m in range(3)):
            print(f"  relu {h}" + "".join(f"{nb_tags_genre[(h, m)]:>7}" for m in range(3)))
    print("\nConfusions les plus fréquentes (relu -> modèle) :")
    for (h, m), c in confusions.most_common(12):
        print(f"  {c:4}  {h}  ->  {m}")
    if genres_attendus:
        print(f"\nGenres fins (genres Spotify masqués, {par_condition[True][0]} morceaux) : "
              f"précision {pct(genres_vp, genres_suggeres)}, rappel {pct(genres_vp, genres_attendus)}")

    out_path = OUTPUT_DIR / f"eval_tags_llm_{datetime.now(timezone.utc):%Y%m%d}.csv"
    with out_path.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=list(detail[0].keys()) if detail else ["id"])
        writer.writeheader()
        writer.writerows(detail)
    print(f"\nDétail par morceau : {out_path}", file=sys.stderr)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--evaluer", action="store_true", help="Mesure la fiabilité sur les morceaux déjà relus")
    parser.add_argument("--modele", default=MODELE_DEFAUT, help=f"Modèle Ollama (défaut : {MODELE_DEFAUT})")
    parser.add_argument("--min-count", type=int, default=10, help="Seuil d'occurrences pour la whitelist de genres")
    parser.add_argument("--exemples", type=int, default=NB_EXEMPLES_DEFAUT, help="Nombre de morceaux relus montrés en exemple")
    parser.add_argument("--reflexion", action="store_true", help="Laisse le modèle raisonner avant de répondre (plus lent)")
    parser.add_argument("--limit", type=int, default=None, help="Ne traite que N morceaux (essai)")
    parser.add_argument("--out", default=None, help="Chemin du CSV de suggestions")
    args = parser.parse_args()

    tracks = charger_tracks()
    relus = [t for t in tracks if t.get("tags")]
    whitelist = construire_whitelist_genres(tracks, args.min_count)
    exemples = choisir_exemples(relus, args.exemples)

    if args.evaluer:
        mode_evaluation(relus, exemples, whitelist, args.modele, args.reflexion, args.limit)
    else:
        mode_suggestion(tracks, exemples, whitelist, args.modele, args.reflexion, args.limit, args.out)


if __name__ == "__main__":
    main()
