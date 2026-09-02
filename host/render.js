"use strict";

// État → DOM, panneau de contrôle (docs/refactor-decisions.md section 2). Seul fichier (avec
// audio.js/timers.js) autorisé à lire le DOM directement en dehors des formulaires locaux de
// config.js. `render(state)` est le seul point d'entrée appelé après chaque notify() — chaque
// fonction de rendu par écran repeint tout son contenu depuis `state`, jamais de mise à jour
// incrémentale ciblée par événement (un seul chemin entre "l'état a changé" et "l'écran est à jour").

import { escapeHtml, libelleCible, libelleReveal, lettreSerie } from "./shared/format.js";
import { avatarHtml, renderScoreList, renderScoreChart, renderJoinQrCode } from "./shared/components.js";

const el = (id) => document.getElementById(id);
const connectionIndicator = el("connection-indicator");
const gameControlsEl = el("game-controls");

const screens = [
  "screen-connect",
  "screen-setup",
  "screen-lobby",
  "screen-serie-intro",
  "screen-round",
  "screen-round-ended",
  "screen-bonus-stake",
  "screen-bonus-question",
  "screen-bonus-result",
  "screen-ended",
];

// Écrans où le lecteur audio + pause/tableau général doivent rester visibles.
const ECRANS_AVEC_CONTROLES = new Set([
  "screen-round",
  "screen-round-ended",
  "screen-bonus-stake",
  "screen-bonus-question",
  "screen-bonus-result",
]);

const SCREEN_ID = {
  lobby: "screen-lobby",
  "serie-intro": "screen-serie-intro",
  round: "screen-round",
  "round-ended": "screen-round-ended",
  "bonus-stake": "screen-bonus-stake",
  "bonus-question": "screen-bonus-question",
  "bonus-result": "screen-bonus-result",
  ended: "screen-ended",
};

const DUREE_TRANSITION_MS = 350;

// Exporté : utilisé aussi par config.js pour les écrans "connect"/"setup", hors du flux réactif
// (formulaires locaux, rien à refléter depuis l'état serveur — voir docs/refactor-decisions.md
// section 2, inputs de configuration exclus du re-render).
export function showScreen(id) {
  gameControlsEl.classList.toggle("hidden", !ECRANS_AVEC_CONTROLES.has(id));

  const actuel = screens.find((s) => !el(s).classList.contains("hidden"));
  if (actuel === id) return;

  if (!actuel) {
    afficherEcran(id);
    return;
  }

  const actuelEl = el(actuel);
  actuelEl.classList.add("screen--leaving");
  setTimeout(() => {
    actuelEl.classList.add("hidden");
    actuelEl.classList.remove("screen--leaving");
    afficherEcran(id);
  }, DUREE_TRANSITION_MS);
}

function afficherEcran(id) {
  for (const s of screens) {
    if (s !== id) el(s).classList.add("hidden");
  }
  const cible = el(id);
  cible.classList.remove("hidden");
  cible.classList.add("screen--entering");
  requestAnimationFrame(() => {
    requestAnimationFrame(() => cible.classList.remove("screen--entering"));
  });
}

export function setConnected(connected) {
  connectionIndicator.textContent = connected ? "connecté" : "déconnecté";
  connectionIndicator.classList.toggle("pill--on", connected);
  connectionIndicator.classList.toggle("pill--off", !connected);
}

function roster(state) {
  return { joueurs: state.players, equipes: state.equipes };
}

function nomJoueur(state, playerId) {
  const p = state.players.find((j) => j.playerId === playerId);
  return p ? p.nom : playerId;
}

function nomEquipe(state, teamId) {
  const eq = state.equipes.find((e) => e.id === teamId);
  return eq ? eq.nom : null;
}

function afficherCover(imgEl, placeholderEl, serverBaseUrl, coverPath) {
  if (coverPath) {
    imgEl.src = `${serverBaseUrl}/files/${coverPath}`;
    imgEl.classList.remove("hidden");
    placeholderEl.classList.add("hidden");
  } else {
    imgEl.classList.add("hidden");
    imgEl.removeAttribute("src");
    placeholderEl.classList.remove("hidden");
  }
}

function renderPlayersList(state) {
  const list = el("lobby-players");
  list.innerHTML = "";
  for (const p of state.players) {
    const li = document.createElement("li");
    if (!p.estConnecte) li.classList.add("disconnected");
    const equipeJoueur = nomEquipe(state, state.equipeParJoueur[p.playerId]);
    const statut = [equipeJoueur, p.estConnecte ? "" : "déconnecté"].filter(Boolean).join(" — ");
    li.innerHTML = `
      <div class="player-row">${avatarHtml(p.playerId, p.nom, undefined, roster(state))}<span>${escapeHtml(p.nom)}</span></div>
      <span>${escapeHtml(statut)}</span>
    `;
    list.appendChild(li);
  }
}

export function renderResults(state) {
  const body = el("results-body");
  body.innerHTML = "";

  for (const r of state.dernierResultats) {
    const tr = document.createElement("tr");
    const reponseAffichee = r.reponse ? r.reponse : "(absent)";
    const classe = r.estCorrecte ? "correct" : "incorrect";
    tr.innerHTML = `
      <td>${escapeHtml(nomJoueur(state, r.playerId))}</td>
      <td>${escapeHtml(reponseAffichee)}</td>
      <td class="${classe}">${r.estCorrecte ? "oui" : "non"}</td>
      <td>${r.points}</td>
      <td></td>
    `;

    // Override manuel : utile pour les réponses tapées ambiguës (tolérance Levenshtein pas
    // toujours adaptée) — inutile pour un QCM (choix strict) ou une première lettre (déjà
    // binaire). Le morceau doit avoir été répondu (pas de mise à jour possible sinon).
    if (state.dernierModeRound === "TapeReponse" && r.reponse) {
      const cellOverride = tr.lastElementChild;
      const btnOui = document.createElement("button");
      btnOui.className = "btn-override btn-override--oui";
      btnOui.textContent = "✓";
      btnOui.title = "Marquer correct";
      btnOui.dataset.playerId = r.playerId;
      btnOui.dataset.estCorrecte = "true";

      const btnNon = document.createElement("button");
      btnNon.className = "btn-override btn-override--non";
      btnNon.textContent = "✗";
      btnNon.title = "Marquer incorrect";
      btnNon.dataset.playerId = r.playerId;
      btnNon.dataset.estCorrecte = "false";

      cellOverride.appendChild(btnOui);
      cellOverride.appendChild(btnNon);
    }

    body.appendChild(tr);
  }
}

function renderBonusPaliers(paliers) {
  const list = el("bonus-paliers");
  list.innerHTML = "";
  paliers.forEach((valeur, index) => {
    const li = document.createElement("li");
    li.innerHTML = `<span>Palier ${index + 1}${index === 0 ? " (safe)" : ""}</span><span>${valeur} pts</span>`;
    list.appendChild(li);
  });
}

function renderBonusResults(state, resultats) {
  const body = el("bonus-results-body");
  body.innerHTML = "";

  for (const r of resultats) {
    const tr = document.createElement("tr");
    const reponseAffichee = r.reponse ? r.reponse : "(absent)";
    const classe = r.estCorrecte ? "correct" : "incorrect";
    tr.innerHTML = `
      <td>${escapeHtml(nomJoueur(state, r.playerId))}</td>
      <td>${r.mise}</td>
      <td>${escapeHtml(reponseAffichee)}</td>
      <td class="${classe}">${r.estCorrecte ? "oui" : "non"}</td>
      <td>${r.points}</td>
    `;
    body.appendChild(tr);
  }
}

// ----- Rendu par écran -----

function renderLobby(state) {
  el("lobby-code").textContent = state.gameCode ?? "";
  renderJoinQrCode(el("lobby-qrcode"), state.serverBaseUrl, state.gameCode);
  renderPlayersList(state);
  el("btn-start-round").disabled = !state.partieConfiguree;
}

function renderSerieIntro(state) {
  el("serie-intro-lettre").textContent = `Série ${state.currentSerieIntroInfo.lettre ?? ""}`;
  el("serie-intro-theme").textContent = state.currentSerieIntroInfo.theme ?? "";
  el("serie-intro-error").textContent = "";
}

function renderRound(state) {
  el("round-error").textContent = "";
  const { mode, cible, serieLabel } = state.currentRoundInfo;
  el("round-mode-label").textContent = `${mode} — trouver ${libelleCible(cible)}${serieLabel ?? ""}`;
  renderScoreList(el("round-scores"), state.dernierScoreDto ?? { joueurs: [] }, roster(state));
}

function renderRoundEnded(state) {
  const { titre, sousTitre } = libelleReveal(state.currentRevealInfo);
  el("reveal-title").textContent = titre;
  el("reveal-artist").textContent = sousTitre;
  afficherCover(el("reveal-cover"), el("reveal-cover-placeholder"), state.serverBaseUrl, state.currentRevealInfo.coverPath);
  el("round-ended-note").textContent = "";
  renderResults(state);
}

function renderBonusStake(state) {
  el("bonus-stake-title").textContent = `Question bonus — mise à l'aveugle${state.currentBonusInfo.serieLabel ?? ""}`;
  renderBonusPaliers(state.currentBonusInfo.paliers ?? []);
}

function renderBonusQuestion(state) {
  el("bonus-question-title").textContent = `Question bonus — à deviner !${state.currentBonusInfo.serieLabel ?? ""}`;
  const { mode, cible, estCourse } = state.currentBonusInfo;
  el("bonus-question-mode-label").textContent = `${mode} — trouver ${libelleCible(cible)}`;
  el("bonus-course-banner").classList.toggle("hidden", !estCourse);
  el("screen-bonus-question").classList.toggle("screen--course", !!estCourse);
}

function renderBonusResult(state) {
  const { titre, sousTitre } = libelleReveal(state.currentRevealInfo);
  el("bonus-reveal-title").textContent = titre;
  el("bonus-reveal-artist").textContent = sousTitre;
  afficherCover(el("bonus-reveal-cover"), el("bonus-reveal-cover-placeholder"), state.serverBaseUrl, state.currentRevealInfo.coverPath);
  renderBonusResults(state, state.currentRevealInfo.resultats ?? []);
  el("bonus-result-course-banner").classList.toggle("hidden", !state.currentRevealInfo.estCourse);
  el("bonus-result-title").textContent = `Résultat de la question bonus${state.currentRevealInfo.serieLabel ?? ""}`;

  // Le thème de la série suivante n'est pas encore connu à cet instant (annoncé seulement par
  // SerieAnnoncee au moment où le host y bascule réellement, voir handlers.js:onSerieAnnoncee et
  // docs/refactor-decisions.md section 1) — seule la lettre est affichée ici, le thème complet
  // apparaît juste après sur l'écran d'annonce de série.
  const seriesRestantes = state.serieCouranteIndex < state.nombreSeriesTotal;
  el("btn-end-now").textContent = seriesRestantes
    ? `Série suivante maintenant (Série ${lettreSerie(state.serieCouranteIndex)})`
    : "Terminer maintenant";
}

function renderEnded(state) {
  renderScoreList(el("final-scores"), state.currentScoresInfo, roster(state), { medailles: true });
  renderScoreChart(el("score-chart"), state.scoreHistory, state.currentScoresInfo, roster(state));
}

const RENDERERS = {
  lobby: renderLobby,
  "serie-intro": renderSerieIntro,
  round: renderRound,
  "round-ended": renderRoundEnded,
  "bonus-stake": renderBonusStake,
  "bonus-question": renderBonusQuestion,
  "bonus-result": renderBonusResult,
  ended: renderEnded,
};

export function render(state) {
  if (!state.leaderboardOpen) {
    el("leaderboard-overlay").classList.add("hidden");
  } else {
    renderScoreList(el("leaderboard-scores"), state.lastLeaderboardDto, roster(state), { medailles: true });
    el("leaderboard-overlay").classList.remove("hidden");
  }

  if (!state.currentScreen) return; // encore sur connect/setup, piloté par config.js

  el("btn-pause").classList.toggle("hidden", state.jeuEnPause);
  el("btn-resume").classList.toggle("hidden", !state.jeuEnPause);

  showScreen(SCREEN_ID[state.currentScreen]);
  RENDERERS[state.currentScreen]?.(state);
}
