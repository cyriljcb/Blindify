"""
Génère une page HTML locale de relecture des tags (aucun serveur requis) à partir d'un CSV de
suggestions — migration_tags_genres.csv, relecture_genres.csv, sortie de suggest_tags_llm.py...
Retour utilisateur du 2026-10-25 : le CSV brut est illisible au tableur, et juger un genre se
fait bien mieux en écoutant le morceau.

La page lit l'audio et la pochette directement depuis data/audio et data/covers (chemins
relatifs, via filePath/coverPath de tracks.json), propose les tags en boutons à cocher, et
exporte un CSV réimportable tel quel par import_tags_csv.py (seule la colonne "tags" change).
L'année est aussi corrigeable (retour du 2026-10-25 : `year` vient de la date de l'ALBUM Spotify,
fausse pour les compilations, ex. Sick and Tired d'Anastacia en 1999 au lieu de 2004) : les années
modifiées partent dans un second CSV au format de apply_reissue_years.py, qui met aussi à jour le
tag décennie. L'année de sortie estimée par le modèle (cache de suggest_tags_llm.py) est affichée
à titre d'indice quand elle diffère.

Les choix sont sauvegardés au fur et à mesure dans le localStorage du navigateur (clé dérivée du
nom du CSV) : fermer la page ou la régénérer ne perd rien. N'écrit JAMAIS dans tracks.json.

Usage :
    python build_relecture_html.py output/migration_tags_genres.csv [--out CHEMIN.html]
Puis ouvrir la page générée (par défaut à côté du CSV) dans le navigateur.
"""

from __future__ import annotations

import argparse
import csv
import json
import os
import sys
from pathlib import Path

from suggest_tags_llm import CACHE_PATH, PROMPT_VERSION, TAG_DISNEY, TAGS_DECENNIE, TAGS_GENRE_LARGE, charger_tracks

SCRIPTS_DIR = Path(__file__).parent
DATA_DIR = SCRIPTS_DIR.parent


def chemin_relatif(cible: Path, depuis: Path) -> str:
    return Path(os.path.relpath(cible, depuis)).as_posix()


def annees_du_modele() -> dict[str, int]:
    """anneeOriginale proposée par le modèle pour la version courante du prompt, par id."""
    annees: dict[str, int] = {}
    if not CACHE_PATH.exists():
        return annees
    for ligne in CACHE_PATH.read_text(encoding="utf-8").splitlines():
        entree = json.loads(ligne)
        if f"|v{PROMPT_VERSION}|" in entree["cle"] and entree["reponse"].get("anneeOriginale"):
            annees[entree["cle"].split("|")[0]] = int(entree["reponse"]["anneeOriginale"])
    return annees


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("csv_path", type=Path, help="CSV avec au moins les colonnes id et tags")
    parser.add_argument("--out", type=Path, help="Page HTML générée (déf. : à côté du CSV, même nom)")
    args = parser.parse_args()

    if not args.csv_path.exists():
        sys.exit(f"{args.csv_path} introuvable.")
    out = (args.out or args.csv_path.with_suffix(".html")).resolve()

    with args.csv_path.open("r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        colonnes = reader.fieldnames or []
        lignes = list(reader)
    if "id" not in colonnes or "tags" not in colonnes:
        sys.exit("Le CSV doit contenir les colonnes id et tags.")

    tracks_par_id = {t["id"]: t for t in charger_tracks()}
    annees_modele = annees_du_modele()
    for ligne in lignes:
        track = tracks_par_id.get(ligne["id"], {})
        ligne["_anneeModele"] = annees_modele.get(ligne["id"], "")
        ligne["_audio"] = chemin_relatif(DATA_DIR / track["filePath"], out.parent) if track.get("filePath") else ""
        ligne["_cover"] = chemin_relatif(DATA_DIR / track["coverPath"], out.parent) if track.get("coverPath") else ""

    donnees = {
        "source": args.csv_path.name,
        "colonnes": colonnes,
        "lignes": lignes,
        "decennies": TAGS_DECENNIE,
        "genres": TAGS_GENRE_LARGE,
        "disney": TAG_DISNEY,
    }
    html = TEMPLATE.replace("__DONNEES__", json.dumps(donnees, ensure_ascii=False).replace("</", "<\\/"))
    out.write_text(html, encoding="utf-8")
    print(f"{len(lignes)} morceau(x) -> {out}", file=sys.stderr)


TEMPLATE = r"""<!doctype html>
<html lang="fr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Relecture des tags</title>
<style>
  :root {
    --fond: #f6f5f2; --panneau: #ffffff; --texte: #1d1c1a; --doux: #6b6862; --bord: #e2dfd8;
    --accent: #2f6fdb; --accent-doux: #e3ecfb; --ok: #2e8b57; --ok-doux: #e1f2e8;
    --alerte: #c77700; --alerte-doux: #fbefd9; --retire: #b3261e;
  }
  @media (prefers-color-scheme: dark) {
    :root {
      --fond: #161614; --panneau: #201f1d; --texte: #ecebe7; --doux: #9c9890; --bord: #34322e;
      --accent: #6b9cf0; --accent-doux: #22314d; --ok: #5cc28a; --ok-doux: #1d3527;
      --alerte: #f0a63c; --alerte-doux: #3d2e14; --retire: #f08a82;
    }
  }
  * { box-sizing: border-box; }
  body { margin: 0; font: 14px/1.45 system-ui, sans-serif; background: var(--fond); color: var(--texte);
         display: grid; grid-template-columns: 340px 1fr; height: 100vh; }
  aside { border-right: 1px solid var(--bord); display: flex; flex-direction: column; min-height: 0; background: var(--panneau); }
  .outils { padding: 12px; border-bottom: 1px solid var(--bord); display: grid; gap: 8px; }
  .outils input, .outils select { width: 100%; padding: 6px 8px; border: 1px solid var(--bord); border-radius: 6px;
         background: var(--fond); color: var(--texte); font: inherit; }
  .progression { font-size: 12px; color: var(--doux); }
  .barre { height: 4px; background: var(--bord); border-radius: 2px; overflow: hidden; }
  .barre div { height: 100%; background: var(--ok); }
  ul { list-style: none; margin: 0; padding: 0; overflow-y: auto; flex: 1; }
  li { padding: 8px 12px; border-bottom: 1px solid var(--bord); cursor: pointer; display: flex; gap: 8px; align-items: center; }
  li:hover { background: var(--fond); }
  li.actif { background: var(--accent-doux); }
  li .t { flex: 1; min-width: 0; }
  li .t b, li .t span { display: block; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  li .t span { color: var(--doux); font-size: 12px; }
  .pastille { width: 10px; height: 10px; border-radius: 50%; flex: none; border: 2px solid var(--bord); }
  .pastille.verif { border-color: var(--alerte); }
  .pastille.fait { background: var(--ok); border-color: var(--ok); }
  main { overflow-y: auto; padding: 24px 32px; }
  .entete { display: flex; gap: 20px; align-items: flex-start; }
  .entete img { width: 160px; height: 160px; object-fit: cover; border-radius: 8px; background: var(--bord); flex: none; }
  h1 { margin: 0 0 4px; font-size: 24px; }
  .meta { color: var(--doux); }
  .annee { margin-top: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; color: var(--doux); }
  .annee input { width: 80px; padding: 4px 6px; border: 1px solid var(--bord); border-radius: 6px; font: inherit;
         background: var(--fond); color: var(--texte); }
  .annee input.modifiee { border-color: var(--alerte); background: var(--alerte-doux); }
  .annee .indice { cursor: pointer; text-decoration: underline dotted; }
  .annee a { color: var(--accent); }
  .badge { display: inline-block; padding: 1px 8px; border-radius: 10px; font-size: 12px; margin-right: 4px; }
  .badge.verif { background: var(--alerte-doux); color: var(--alerte); }
  .badge.fait { background: var(--ok-doux); color: var(--ok); }
  audio { width: 100%; margin: 16px 0 4px; }
  .lecture { display: flex; gap: 6px; flex-wrap: wrap; }
  section { margin-top: 20px; }
  h2 { font-size: 12px; text-transform: uppercase; letter-spacing: .06em; color: var(--doux); margin: 0 0 8px; }
  .puces { display: flex; flex-wrap: wrap; gap: 6px; }
  button { font: inherit; padding: 6px 12px; border-radius: 16px; border: 1px solid var(--bord); background: var(--panneau);
           color: var(--texte); cursor: pointer; }
  button:hover { border-color: var(--accent); }
  button.on { background: var(--accent); border-color: var(--accent); color: #fff; }
  button.avant::after { content: " ·"; color: var(--doux); }
  button.on.avant::after { content: ""; }
  button.retire { border-style: dashed; color: var(--retire); text-decoration: line-through; }
  button kbd { font-size: 11px; opacity: .6; margin-left: 4px; }
  .actions { display: flex; gap: 8px; flex-wrap: wrap; margin-top: 24px; }
  .actions .principal { background: var(--ok); border-color: var(--ok); color: #fff; }
  .info { background: var(--panneau); border: 1px solid var(--bord); border-radius: 8px; padding: 10px 12px; }
  .info div + div { margin-top: 4px; }
  .info b { color: var(--doux); font-weight: 500; }
  .aide { margin-top: 28px; font-size: 12px; color: var(--doux); }
  .vide { color: var(--doux); padding: 40px; text-align: center; }
  @media (max-width: 800px) { body { grid-template-columns: 1fr; height: auto; } aside { max-height: 40vh; } main { padding: 16px; } }
</style>
</head>
<body>
<aside>
  <div class="outils">
    <input id="recherche" placeholder="Rechercher titre / artiste…">
    <select id="filtre">
      <option value="tous">Tous les morceaux</option>
      <option value="verif">À vérifier seulement</option>
      <option value="restant">Pas encore validés</option>
    </select>
    <div class="progression" id="progression"></div>
    <div class="barre"><div id="barre"></div></div>
    <button id="exporter">Exporter le CSV relu</button>
  </div>
  <ul id="liste"></ul>
</aside>
<main id="detail"></main>
<script>
const D = __DONNEES__;
const CLE = "blindify-relecture-" + D.source;
const lignes = D.lignes;
let etat = {};
try { etat = JSON.parse(localStorage.getItem(CLE) || "{}"); } catch (e) {}
const sauver = () => { try { localStorage.setItem(CLE, JSON.stringify(etat)); } catch (e) {} };

const liste = (s) => (s || "").split(";").map(x => x.trim()).filter(Boolean);
const tagsDe = (l) => etat[l.id]?.tags ?? liste(l.tags);
const fait = (l) => !!etat[l.id]?.valide;
// relecture_genres.csv (audit_tags_llm.py) n'a pas de colonne aVerifier : un écart de famille en tient lieu.
const aVerifier = (l) => (l.aVerifier || "").toLowerCase() === "oui" || l.ecart === "famille";
const anneeDe = (l) => etat[l.id]?.annee ?? (parseInt(l.year, 10) || null);
const anneeModifiee = (l) => etat[l.id]?.annee != null && etat[l.id].annee !== parseInt(l.year, 10);
const decennieDe = (a) => a < 1970 ? "avant-1970" : "annees-" + Math.min(Math.floor(a / 10) * 10, 2020);

function changerAnnee(l, annee) {
  if (!annee || annee < 1900 || annee > 2100) return;
  etat[l.id] = { ...(etat[l.id] || {}), annee };
  // Même convention que apply_reissue_years.py : la décennie suit l'année.
  modifier(l, ts => [decennieDe(annee), ...ts.filter(x => !D.decennies.includes(x))]);
}

let courant = 0;
let visibles = [];

function filtrer() {
  const q = document.getElementById("recherche").value.toLowerCase();
  const f = document.getElementById("filtre").value;
  visibles = lignes.map((l, i) => i).filter(i => {
    const l = lignes[i];
    if (f === "verif" && !aVerifier(l)) return false;
    if (f === "restant" && fait(l)) return false;
    return !q || (l.title + " " + l.artist).toLowerCase().includes(q);
  });
}

function rendreListe() {
  const ul = document.getElementById("liste");
  ul.innerHTML = "";
  for (const i of visibles) {
    const l = lignes[i];
    const li = document.createElement("li");
    if (i === courant) li.className = "actif";
    li.innerHTML = `<span class="pastille ${fait(l) ? "fait" : aVerifier(l) ? "verif" : ""}"></span>
      <span class="t"><b></b><span></span></span>`;
    li.querySelector("b").textContent = l.title;
    li.querySelector(".t span").textContent = l.artist + (l.year ? " · " + l.year : "");
    li.onclick = () => choisir(i);
    ul.appendChild(li);
  }
  const n = lignes.filter(fait).length;
  document.getElementById("progression").textContent =
    `${n} / ${lignes.length} validés · ${lignes.filter(aVerifier).length} à vérifier`;
  document.getElementById("barre").style.width = (100 * n / lignes.length) + "%";
  ul.querySelector(".actif")?.scrollIntoView({ block: "nearest" });
}

function puce(tag, actifs, avant, raccourci) {
  const b = document.createElement("button");
  const on = actifs.includes(tag);
  b.className = (on ? "on " : "") + (avant.includes(tag) ? "avant " : "") + (!on && avant.includes(tag) ? "retire" : "");
  b.innerHTML = tag + (raccourci ? `<kbd>${raccourci}</kbd>` : "");
  b.title = avant.includes(tag) ? "présent dans les tags actuels" : "";
  return b;
}

function modifier(l, f) {
  const tags = f([...tagsDe(l)]);
  etat[l.id] = { ...(etat[l.id] || {}), tags };
  sauver();
  rendreDetail();
}

function rendreDetail() {
  const m = document.getElementById("detail");
  const l = lignes[courant];
  if (!l) { m.innerHTML = `<div class="vide">Aucun morceau.</div>`; return; }
  const tags = tagsDe(l);
  const avant = liste(l.tagsActuels);
  const audio = m.querySelector("audio");
  const memeMorceau = audio && audio.dataset.id === l.id;

  if (!memeMorceau) {
    m.innerHTML = `
      <div class="entete"><img alt=""><div>
        <div id="badges"></div><h1></h1><div class="meta"></div>
        <div class="annee">Année <input id="annee" type="number" min="1900" max="2100">
          <span id="indiceAnnee"></span>
          <a id="chercher" target="_blank" rel="noopener">vérifier sur le web ↗</a></div></div></div>
      <audio controls preload="auto"></audio>
      <div class="lecture">
        <button data-saut="-10">−10 s <kbd>←</kbd></button>
        <button data-saut="10">+10 s <kbd>→</kbd></button>
        <button data-pos="0.15">Début</button>
        <button data-pos="0.35">Refrain probable</button>
        <button data-pos="0.6">Milieu</button>
      </div>
      <div id="tags"></div>
      <section><h2>Contexte</h2><div class="info" id="info"></div></section>
      <div class="actions">
        <button class="principal" id="valider">Valider et suivant <kbd>Entrée</kbd></button>
        <button id="modele" hidden>Prendre la suggestion du modèle <kbd>M</kbd></button>
        <button id="proposition">Revenir à la proposition</button>
        <button id="actuels">Remettre les tags actuels</button>
      </div>
      <div class="aide">Raccourcis : Espace lecture/pause · ↑ ↓ morceau précédent/suivant · ← → avance de 10 s ·
        1–9, 0 cochent les genres dans l'ordre · M prend la suggestion du modèle · Entrée valide.
        Corriger l'année met aussi à jour la décennie. Les puces barrées sont des tags actuels retirés
        par la proposition. Cocher disney retire les genres (et inversement). Tout est sauvegardé automatiquement dans ce navigateur.</div>`;
    const img = m.querySelector("img");
    if (l._cover) img.src = l._cover; else img.style.visibility = "hidden";
    m.querySelector("h1").textContent = l.title;
    m.querySelector(".meta").textContent = [l.artist, l.album].filter(Boolean).join(" · ");
    m.querySelector("#chercher").href = "https://www.google.com/search?q=" +
      encodeURIComponent(`${l.title} ${l.artist} date de sortie`);
    const champ = m.querySelector("#annee");
    champ.onchange = () => changerAnnee(l, parseInt(champ.value, 10));
    const indice = m.querySelector("#indiceAnnee");
    if (l._anneeModele && String(l._anneeModele) !== String(l.year)) {
      indice.innerHTML = `le modèle pense <span class="indice"></span>`;
      indice.querySelector(".indice").textContent = l._anneeModele;
      indice.querySelector(".indice").onclick = () => changerAnnee(l, +l._anneeModele);
    }
    const a = m.querySelector("audio");
    a.dataset.id = l.id;
    if (l._audio) {
      a.src = l._audio;
      a.addEventListener("loadedmetadata", () => { a.currentTime = a.duration * 0.35; a.play().catch(() => {}); }, { once: true });
    }
    m.querySelectorAll("[data-saut]").forEach(b => b.onclick = () => { a.currentTime += +b.dataset.saut; });
    m.querySelectorAll("[data-pos]").forEach(b => b.onclick = () => { a.currentTime = a.duration * b.dataset.pos; a.play(); });
    const info = m.querySelector("#info");
    const masquees = new Set(["id", "title", "artist", "album", "year", "tags", "aVerifier"]);
    for (const c of D.colonnes.filter(c => !masquees.has(c) && l[c])) {
      const div = document.createElement("div");
      div.innerHTML = "<b></b> ";
      div.querySelector("b").textContent = c + " :";
      div.append(l[c]);
      info.appendChild(div);
    }
    m.querySelector("#valider").onclick = valider;
    m.querySelector("#proposition").onclick = () => { delete etat[l.id]; sauver(); rendreDetail(); rendreListe(); };
    m.querySelector("#actuels").onclick = () => modifier(l, () => avant);
    const boutonModele = m.querySelector("#modele");
    boutonModele.hidden = !l.suggestionModele;
    boutonModele.onclick = () => modifier(l, () => liste(l.suggestionModele));
  }

  const champAnnee = m.querySelector("#annee");
  if (document.activeElement !== champAnnee) champAnnee.value = anneeDe(l) || "";
  champAnnee.classList.toggle("modifiee", anneeModifiee(l));
  m.querySelector("#badges").innerHTML =
    (fait(l) ? `<span class="badge fait">validé</span>` : "") + (aVerifier(l) ? `<span class="badge verif">à vérifier</span>` : "");

  const zone = m.querySelector("#tags");
  zone.innerHTML = "";
  const groupe = (titre, valeurs, clic, raccourcis) => {
    const s = document.createElement("section");
    s.innerHTML = `<h2>${titre}</h2><div class="puces"></div>`;
    valeurs.forEach((t, k) => {
      const b = puce(t, tags, avant, raccourcis ? raccourcis[k] : "");
      b.onclick = () => clic(t);
      s.querySelector(".puces").appendChild(b);
    });
    zone.appendChild(s);
  };
  const touches = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "0"];
  groupe("Genres", D.genres, t => basculerGenre(l, t), touches);
  groupe("Décennie", D.decennies, t => modifier(l, ts => ts.includes(t) ? ts.filter(x => x !== t) : [...ts.filter(x => !D.decennies.includes(x)), t]));
  const connus = new Set([...D.genres, ...D.decennies]);
  const autres = [...new Set([D.disney, ...tags, ...avant].filter(t => !connus.has(t)))];
  groupe("Autres tags", autres, t => modifier(l, ts => ts.includes(t) ? ts.filter(x => x !== t)
    // Règle du 2026-10-25 : un morceau disney ne porte aucun genre large.
    : t === D.disney ? [...ts.filter(x => !D.genres.includes(x)), t] : [...ts, t]));
}

function basculerGenre(l, t) {
  modifier(l, ts => ts.includes(t) ? ts.filter(x => x !== t) : [...ts.filter(x => x !== D.disney), t]);
}

function choisir(i) { courant = i; rendreDetail(); rendreListe(); }

function deplacer(pas) {
  const pos = visibles.indexOf(courant);
  const cible = visibles[pos === -1 ? 0 : pos + pas];
  if (cible !== undefined) choisir(cible);
}

function valider() {
  const l = lignes[courant];
  etat[l.id] = { ...(etat[l.id] || {}), tags: tagsDe(l), valide: true };
  sauver();
  filtrer();
  choisir(visibles.find(i => i > courant) ?? visibles[0] ?? courant);
}

function exporter() {
  const echap = v => /[",\n;]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v;
  const rangs = [D.colonnes.join(",")];
  for (const l of lignes) {
    rangs.push(D.colonnes.map(c => echap(c === "tags" ? tagsDe(l).join("; ") : (l[c] ?? ""))).join(","));
  }
  const base = D.source.replace(/\.csv$/i, "");
  telecharger(rangs, base + "_relu.csv");
  // Années corrigées : format de apply_reissue_years.py (id + year_probable), seulement les lignes modifiées.
  const annees = lignes.filter(anneeModifiee);
  if (annees.length) {
    telecharger(["id,title,artist,year,year_probable",
      ...annees.map(l => [l.id, l.title, l.artist, l.year, anneeDe(l)].map(v => echap(String(v ?? ""))).join(","))],
      base + "_annees.csv");
  }
}

function telecharger(rangs, nom) {
  const blob = new Blob(["﻿" + rangs.join("\r\n")], { type: "text/csv;charset=utf-8" });
  const a = document.createElement("a");
  a.href = URL.createObjectURL(blob);
  a.download = nom;
  a.click();
}

document.getElementById("recherche").oninput = () => { filtrer(); rendreListe(); };
document.getElementById("filtre").onchange = () => { filtrer(); rendreListe(); };
document.getElementById("exporter").onclick = exporter;
document.addEventListener("keydown", e => {
  if (e.target.tagName === "INPUT" || e.target.tagName === "SELECT") return;
  const a = document.querySelector("audio");
  if (e.key === " ") { e.preventDefault(); if (a) a.paused ? a.play() : a.pause(); }
  else if (e.key === "ArrowDown") { e.preventDefault(); deplacer(1); }
  else if (e.key === "ArrowUp") { e.preventDefault(); deplacer(-1); }
  else if (e.key === "ArrowRight" && a) { e.preventDefault(); a.currentTime += 10; }
  else if (e.key === "ArrowLeft" && a) { e.preventDefault(); a.currentTime -= 10; }
  else if (e.key === "Enter") { e.preventDefault(); valider(); }
  else if (e.key === "m" || e.key === "M") { document.getElementById("modele")?.click(); }
  else {
    const k = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "0"].indexOf(e.key);
    if (k >= 0 && D.genres[k]) {
      basculerGenre(lignes[courant], D.genres[k]);
    }
  }
});

filtrer();
choisir(visibles[0] ?? 0);
</script>
</body>
</html>
"""


if __name__ == "__main__":
    main()
