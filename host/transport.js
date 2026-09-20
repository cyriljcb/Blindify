"use strict";

// Connexion SignalR + invocations sortantes — ne touche jamais au DOM (docs/refactor-decisions.md
// section 2). handlers.js reçoit la connexion créée ici pour y enregistrer ses `connection.on(...)`,
// mais c'est ce module qui possède le cycle de vie de la connexion elle-même.

let connection = null;

export function getConnection() {
  return connection;
}

// Crée la connexion sans la démarrer — laisse l'appelant enregistrer ses handlers `connection.on(...)`
// avant `demarrerConnexion`, pour ne manquer aucun événement émis juste après le handshake.
export function creerConnexion(serverBaseUrl) {
  connection = new signalR.HubConnectionBuilder()
    .withUrl(`${serverBaseUrl}/hubs/game`, { withCredentials: false })
    .withAutomaticReconnect()
    .build();
  return connection;
}

// [timeoutMs] borne la tentative (utilisé pour la reconnexion auto au démarrage) ; sans borne pour
// un clic manuel sur "Se connecter".
export async function demarrerConnexion(timeoutMs = null) {
  const demarrage = connection.start();
  if (!timeoutMs) return demarrage;

  return Promise.race([
    demarrage,
    new Promise((_, reject) => setTimeout(() => reject(new Error("Délai de connexion dépassé")), timeoutMs)),
  ]);
}

// ----- Invocations sortantes -----

export const invoke = {
  createGame: (payload) => connection.invoke("CreateGame", payload),
  rejoinAsHost: (code, hostSecret) => connection.invoke("RejoinAsHost", code, hostSecret),
  configurerPartie: (payload) => connection.invoke("ConfigurerPartie", payload),
  annoncerSerieCourante: () => connection.invoke("AnnoncerSerieCourante"),
  startRound: () => connection.invoke("StartRound"),
  startBonusRound: () => connection.invoke("StartBonusRound"),
  nextRound: () => connection.invoke("NextRound"),
  showLeaderboard: () => connection.invoke("ShowLeaderboard"),
  validateAnswerManually: (playerId, estCorrecte) => connection.invoke("ValidateAnswerManually", { playerId, estCorrecte }),
  pauseGame: () => connection.invoke("PauseGame"),
  resumeGame: () => connection.invoke("ResumeGame"),
  endGame: () => connection.invoke("EndGame"),
  rejouerPartie: () => connection.invoke("RejouerPartie"),
  // V2, section 12.4 — commentaire: null si vide, jamais une chaîne vide (SignalementRequestDto.Commentaire
  // reste optionnel côté contrat).
  signalerMorceau: (trackId, raison, commentaire) =>
    connection.invoke("SignalerMorceau", { trackId, raison, commentaire: commentaire || null }),
};
