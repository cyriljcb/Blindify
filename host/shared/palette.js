"use strict";

// Palette de couleurs partagée entre host/app.js, host/display.js et app/lib/widgets/player_avatar.dart
// (dupliquée dans ce dernier faute de partage possible entre Dart et JS — voir docs/refactor-decisions.md
// section 7, palette de couleurs). Choisies manuellement pour rester visuellement distinctes entre
// indices voisins (un hash%360 pouvait donner deux teintes de vert à deux joueurs différents).
export const PALETTE_AVATARS = [
  "#E63946", "#457B9D", "#F4A300", "#2A9D8F",
  "#9B5DE5", "#06D6A0", "#F15BB5", "#4CC9F0",
  "#FF6B35", "#8AC926", "#FFCA3A", "#6A4C93",
];

// Couleurs des 3 premiers au classement final, identiques aux bordures/ombres appliquées par
// .score-list--final li:nth-child(1/2/3) (style.css) — retour utilisateur : le graphique "qui
// menait quand" utilisait la couleur d'avatar (ordre d'arrivée), différente de la couleur "or"
// du podium juste au-dessus, déroutant pour se retrouver dans la légende.
export const COULEURS_PODIUM = ["var(--mustard)", "var(--ink-dim)", "var(--coral)"];

// roster : { joueurs, equipes } — le roster connu de l'appelant (host : players/equipes en direct ;
// display : rosterJoueurs/rosterEquipes reconstruit depuis les messages reçus). Couleur stable par
// identifiant, basée sur la position dans le roster (ordre d'arrivée) plutôt que sur un hash de
// l'id, pour garantir des couleurs distinctes entre joueurs (et entre équipes) tant que leur nombre
// ne dépasse pas la taille de la palette. Recherche d'équipe tolérante (id ?? teamId) — version
// reprise de l'ancienne display.js comme unique version, strictement plus permissive.
export function couleurAvatar(id, roster) {
  const joueurs = roster?.joueurs ?? [];
  const equipes = roster?.equipes ?? [];

  const indexJoueur = joueurs.findIndex((p) => p.playerId === id);
  if (indexJoueur >= 0) return PALETTE_AVATARS[indexJoueur % PALETTE_AVATARS.length];

  const indexEquipe = equipes.findIndex((e) => (e.id ?? e.teamId) === id);
  if (indexEquipe >= 0) return PALETTE_AVATARS[(joueurs.length + indexEquipe) % PALETTE_AVATARS.length];

  let hash = 0;
  for (let i = 0; i < id.length; i++) hash = (hash * 31 + id.charCodeAt(i)) >>> 0;
  return `hsl(${hash % 360}, 65%, 55%)`;
}

export function couleurClassement(id, rang, roster) {
  return rang !== undefined && rang < COULEURS_PODIUM.length ? COULEURS_PODIUM[rang] : couleurAvatar(id, roster);
}
