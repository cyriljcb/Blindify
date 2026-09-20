"use strict";

// Diffusion vers l'écran public (fenêtre séparée, même PC — voir CLAUDE.md). Canal choisi
// explicitement pour éviter les soucis d'origine avec des pages ouvertes en file:// : postMessage
// direct vers la référence de fenêtre obtenue via window.open (BroadcastChannel exigerait une
// origine strictement identique, ce que file:// ne garantit pas d'un navigateur à l'autre).
// Rassemble l'état courant (jamais les infos sensibles : pas de filePath, pas de points/scores hors
// des moments de reveal explicitement voulus — tableau général, fin de partie).

import { timerEndAtCourant, timerDurationMsCourante } from "./timers.js";

const el = (id) => document.getElementById(id);
const displayIndicatorEl = el("display-indicator");

let displayWindow = null;
let lastDisplayState = null;

function setDisplayIndicator(ouvert) {
  displayIndicatorEl.textContent = ouvert ? "écran public : ouvert" : "écran public : fermé";
  displayIndicatorEl.classList.toggle("pill--on", ouvert);
  displayIndicatorEl.classList.toggle("pill--off", !ouvert);
}

export function openDisplayWindow() {
  if (displayWindow && !displayWindow.closed) {
    displayWindow.focus();
    return;
  }
  displayWindow = window.open("display.html", "blindify-display", "width=1280,height=800");
  setDisplayIndicator(true);
}

setInterval(() => {
  if (displayWindow) setDisplayIndicator(!displayWindow.closed);
}, 1000);

export function sendToDisplay(msg) {
  if (displayWindow && !displayWindow.closed) displayWindow.postMessage(msg, "*");
}

export function syncDisplay(state) {
  const msg = {
    type: "state",
    screen: state.currentDisplayScreen,
    gameCode: state.gameCode,
    serverBaseUrl: state.serverBaseUrl,
    paused: state.jeuEnPause,
    players: state.players.map((p) => ({ playerId: p.playerId, nom: p.nom, estConnecte: p.estConnecte })),
    equipeParJoueur: state.equipeParJoueur,
    equipes: state.equipes,
    timerEndAt: timerEndAtCourant(),
    timerDurationMs: timerDurationMsCourante(),
    round: state.currentRoundInfo,
    reveal: state.currentRevealInfo,
    bonus: state.currentBonusInfo,
    scores: state.currentScoresInfo,
    titres: state.currentTitresInfo,
    titreIndexAffiche: state.titreIndexAffiche,
    serieIntro: state.currentSerieIntroInfo,
    // Uniquement pertinent (et présent) au moment du GameEnded — voir renderScoreChart, partagé
    // avec l'écran public pour un rendu identique du graphique.
    scoreHistory: state.scoreHistory,
  };
  lastDisplayState = msg;
  sendToDisplay(msg);
}

export function sendLeaderboardShow(dto) {
  sendToDisplay({ type: "leaderboard-show", scores: dto });
}

export function sendLeaderboardHide() {
  sendToDisplay({ type: "leaderboard-hide" });
}

// Retour utilisateur : réaction visuelle + classement de rapidité sur l'écran public dès qu'un
// joueur répond — jamais l'exactitude de la réponse (voir PlayerAnsweredDto côté backend), jamais
// relayé au téléphone des joueurs (uniquement l'écran public, voir main.js).
export function sendPlayerAnswered({ playerId, tempsEcouleMs }) {
  sendToDisplay({ type: "player-answered", playerId, tempsEcouleMs });
}

// Vide le classement de rapidité affiché — appelé à chaque nouveau round/question bonus (et à la
// fin) pour ne jamais laisser le classement d'un round précédent déborder sur le suivant.
export function resetPlayerAnswered() {
  sendToDisplay({ type: "player-answered-reset" });
}

// onStartRoundRequested : relaie le clic du bouton "Lancer" de l'écran public (retour utilisateur :
// éviter le switch de fenêtre) vers l'orchestration de main.js.
export function initDisplayBridge(state, { onStartRoundRequested }) {
  window.addEventListener("message", (event) => {
    if (event.data?.type === "request-sync") {
      if (lastDisplayState) sendToDisplay(lastDisplayState);
      if (state.leaderboardOpen && state.lastLeaderboardDto) sendLeaderboardShow(state.lastLeaderboardDto);
      return;
    }

    if (event.data?.type === "start-round" && state.currentDisplayScreen === "lobby") {
      onStartRoundRequested();
    }
  });
}
