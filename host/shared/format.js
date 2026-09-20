"use strict";

// Fonctions de formatage texte partagées entre host/app.js (panneau de contrôle) et
// host/display.js (écran public) — voir docs/refactor-decisions.md section 2 étape 1. Chargées par
// les deux pages en modules ES natifs (aucun bundler, servies telles quelles par le backend).

export function escapeHtml(str) {
  const div = document.createElement("div");
  div.textContent = str ?? "";
  return div.innerHTML;
}

export function capitaliser(texte) {
  return texte.length > 0 ? texte.charAt(0).toUpperCase() + texte.slice(1) : texte;
}

const LETTRES_SERIES = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

// Lettrage des séries (retour utilisateur : "la première série sera la série A") — au-delà de Z
// (26 séries, très au-delà de tout usage réel), retombe sur un numéro plutôt que de planter.
export function lettreSerie(index) {
  return LETTRES_SERIES[index] ?? String(index + 1);
}

export function libelleTheme(tags) {
  if (!tags || tags.length === 0) return "Aléatoire";
  return tags.map((t) => capitaliser(t.replace(/-/g, " "))).join(" + ");
}

// Cible Film (morceaux "disney", voir RoundService.DemarrerRound / BonusRoundService.CreerBonusRound) :
// la réponse attendue était le film, pas le titre réel de la chanson — le reveal doit donc mettre
// le film en avant. Le vrai titre/artiste reste affiché en dessous, à titre de bonus trivia. Version
// défensive (optional chaining) — reprise de l'ancienne version de display.js comme unique version,
// couvre aussi bien un payload complet (host) qu'un payload reconstruit depuis postMessage (display).
export function libelleReveal(reveal) {
  if (reveal?.cible === "Film") {
    return { titre: reveal?.film ?? "", sousTitre: `${reveal?.title ?? ""} — ${reveal?.artist ?? ""}` };
  }
  // V2, section 12.5 : met l'année réelle en avant (comme le film pour la cible Film), le titre
  // réel reste affiché en dessous à titre de trivia — annee absente si Track.Year n'était pas
  // renseigné, repli sur le titre dans ce cas plutôt que d'afficher "undefined".
  if (reveal?.cible === "Annee" && reveal?.annee != null) {
    return { titre: `${reveal.annee}`, sousTitre: `${reveal?.title ?? ""} — ${reveal?.artist ?? ""}` };
  }
  return { titre: reveal?.title ?? "", sousTitre: reveal?.artist ?? "" };
}

// Libellé complet "trouve le titre/l'artiste/le film/l'année" — utilisé par le panneau de contrôle
// sur les écrans round/bonus-question. V2, section 12.5 : cible Annee ajoutée — sans branche
// explicite, elle retombait sur "le film" (dernier cas du if/else en cascade), affichant "Trouve le
// film" pendant une question qui demandait l'année.
export function libelleCible(cible) {
  if (cible === "Titre") return "le titre";
  if (cible === "Auteur") return "l'artiste";
  if (cible === "Annee") return "l'année";
  return "le film";
}

// Libellé "(Série B — Rock)" pour les écrans round/bonus — vide si une seule série (pas de
// délimitation à afficher). tagsParSerie : tableau index -> liste de tags, alimenté série par
// série depuis l'événement SerieAnnoncee (le serveur calcule désormais la répartition des thèmes,
// voir docs/refactor-decisions.md section 1).
export function libelleSerie(index, nombreSeriesTotal, tagsParSerie) {
  if (nombreSeriesTotal <= 1) return "";
  return ` (Série ${lettreSerie(index)} — ${libelleTheme(tagsParSerie[index])})`;
}
