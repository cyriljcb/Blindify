"use strict";

import { escapeHtml } from "./format.js";
import { couleurAvatar, couleurClassement } from "./palette.js";

const MEDAILLES = ["🥇", "🥈", "🥉"];

// roster : { joueurs, equipes } — voir palette.js:couleurAvatar. rang (0-indexé) : si fourni et < 3,
// affiche une médaille à la place des initiales (classement final / tableau général uniquement —
// pas pendant un round en cours). tailleMedaille : 1.3rem par défaut (panneau de contrôle), 1.6rem
// sur l'écran public (lu à distance) — fusion des deux versions historiquement dupliquées.
export function avatarHtml(id, nom, rang, roster, { tailleMedaille = "1.3rem" } = {}) {
  if (rang !== undefined && rang < 3) {
    return `<span class="avatar" style="background: transparent; font-size: ${tailleMedaille};">${MEDAILLES[rang]}</span>`;
  }
  const initiale = escapeHtml((nom || "?").trim().slice(0, 1).toUpperCase() || "?");
  return `<span class="avatar" style="background: ${couleurAvatar(id, roster)};">${initiale}</span>`;
}

// Scores complets — uniquement pour le tableau général et la fin de partie, moments de reveal
// volontaire (voir docs/architecture.md section 7). medailles : false pour un score en cours
// (panneau de contrôle uniquement, jamais diffusé à l'écran public), true pour un classement figé.
export function renderScoreList(container, dto, roster, { medailles = false } = {}) {
  container.innerHTML = "";

  const joueurs = [...dto.joueurs].sort((a, b) => b.score - a.score);
  joueurs.forEach((j, index) => {
    const li = document.createElement("li");
    li.innerHTML = `
      <div class="player-row">${avatarHtml(j.playerId, j.nom, medailles ? index : undefined, roster)}<span>${escapeHtml(j.nom)}</span></div>
      <span>${j.score}</span>
    `;
    container.appendChild(li);
  });

  if (dto.equipes && dto.equipes.length > 0) {
    const header = document.createElement("li");
    header.innerHTML = "<strong>Équipes</strong><span></span>";
    container.appendChild(header);

    [...dto.equipes].sort((a, b) => b.score - a.score).forEach((eq, index) => {
      const li = document.createElement("li");
      li.innerHTML = `
        <div class="player-row">${avatarHtml(eq.teamId, eq.nom, medailles ? index : undefined, roster)}<span>${escapeHtml(eq.nom)}</span></div>
        <span>${eq.score}</span>
      `;
      container.appendChild(li);
    });
  }
}

// Graphique "qui menait quand" — un point par série résolue (voir state.js:scoreHistory), plus un
// point de départ (tout le monde à 0) et le score final. Dessiné en SVG à la main (pas de
// bibliothèque externe, cohérent avec le reste de host/) — une ligne par joueur/équipe, couleur
// alignée sur les avatars utilisés partout ailleurs. Partagé entre le panneau de contrôle et l'écran
// public (reçu via syncDisplay côté display.js plutôt que redessiné indépendamment) pour garantir un
// rendu identique.
export function renderScoreChart(container, history, finalDto, roster) {
  container.innerHTML = "";
  if (!history || history.length === 0) return; // partie terminée avant le moindre round résolu

  const useEquipes = finalDto.equipes && finalDto.equipes.length > 0;
  const depart = {
    label: "Départ",
    joueurs: (roster?.joueurs ?? []).map((p) => ({ playerId: p.playerId, score: 0 })),
    equipes: (roster?.equipes ?? []).map((e) => ({ teamId: e.id ?? e.teamId, score: 0 })),
  };
  const points = [depart, ...history, { label: "Final", joueurs: finalDto.joueurs, equipes: finalDto.equipes }];
  const series = useEquipes
    ? finalDto.equipes.map((e) => ({ id: e.teamId, nom: e.nom }))
    : finalDto.joueurs.map((j) => ({ id: j.playerId, nom: j.nom }));

  // Rang final (0-indexé) par id — même tri que renderScoreList({ medailles: true }) — pour
  // aligner les couleurs du graphique sur celles du podium plutôt que l'ordre d'arrivée.
  const classementFinal = [...(useEquipes ? finalDto.equipes : finalDto.joueurs)]
    .sort((a, b) => b.score - a.score)
    .map((x) => (useEquipes ? x.teamId : x.playerId));

  const width = 900;
  const height = 320;
  const padding = { top: 20, right: 24, bottom: 36, left: 44 };
  const plotW = width - padding.left - padding.right;
  const plotH = height - padding.top - padding.bottom;

  const valeurs = points.flatMap((p) => (useEquipes ? p.equipes : p.joueurs).map((x) => x.score));
  const minScore = Math.min(0, ...valeurs);
  const maxScore = Math.max(0, ...valeurs);
  const range = maxScore - minScore || 1;

  const posX = (i) => padding.left + (points.length === 1 ? 0 : (i / (points.length - 1)) * plotW);
  const posY = (v) => padding.top + plotH - ((v - minScore) / range) * plotH;

  let svg = `<svg viewBox="0 0 ${width} ${height}" class="score-chart-svg" preserveAspectRatio="xMidYMid meet">`;

  if (minScore < 0) {
    svg += `<line x1="${padding.left}" y1="${posY(0)}" x2="${width - padding.right}" y2="${posY(0)}" class="score-chart-zero" />`;
  }

  points.forEach((p, i) => {
    svg += `<text x="${posX(i)}" y="${height - 12}" class="score-chart-axis-label" text-anchor="middle">${escapeHtml(p.label)}</text>`;
  });

  for (const s of series) {
    const couleur = couleurClassement(s.id, classementFinal.indexOf(s.id), roster);
    const coords = points.map((p, i) => {
      const pool = useEquipes ? p.equipes : p.joueurs;
      const entree = pool.find((x) => (useEquipes ? x.teamId : x.playerId) === s.id);
      return [posX(i), posY(entree ? entree.score : 0)];
    });
    svg += `<polyline points="${coords.map(([x, y]) => `${x},${y}`).join(" ")}" class="score-chart-line" style="stroke: ${couleur}" />`;
    for (const [cx, cy] of coords) {
      svg += `<circle cx="${cx}" cy="${cy}" r="4.5" class="score-chart-dot" style="fill: ${couleur}" />`;
    }
  }

  svg += `</svg>`;

  const legende = series
    .map(
      (s) => `
    <span class="score-chart-legend-item">
      <span class="score-chart-legend-swatch" style="background: ${couleurClassement(s.id, classementFinal.indexOf(s.id), roster)}"></span>
      ${escapeHtml(s.nom)}
    </span>`
    )
    .join("");

  container.innerHTML = `${svg}<div class="score-chart-legend">${legende}</div>`;
}

// Mémoïsation par module — host/index.html et host/display.html sont deux documents séparés (l'un
// ouvert via window.open), donc deux instances indépendantes de ce module : pas de collision entre
// les deux QR codes malgré la même clé de cache.
let dernierQrKey = null;

export function renderJoinQrCode(container, serverUrl, code) {
  if (!container || !code || !serverUrl) return;

  const key = serverUrl + "|" + code;
  if (key === dernierQrKey) return;
  dernierQrKey = key;

  const payload = JSON.stringify({ server: serverUrl, code });
  const qr = qrcode(0, "M");
  qr.addData(payload);
  qr.make();
  container.innerHTML = qr.createSvgTag({ cellSize: 6, margin: 4, scalable: true });
}
