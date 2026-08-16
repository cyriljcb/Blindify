"use strict";

// ----- État global -----

let connection = null;
let serverBaseUrl = "";
let gameCode = null;
let players = []; // { playerId, nom, estConnecte }

let timerInterval = null;
let timerEndAt = null;
let timerDurationMs = 0;
let timerPausedRemainingMs = null;

// ----- Éléments DOM -----

const el = (id) => document.getElementById(id);

const connectionIndicator = el("connection-indicator");
const screens = [
  "screen-connect",
  "screen-setup",
  "screen-lobby",
  "screen-round",
  "screen-round-ended",
  "screen-ended",
];

const audioEl = el("player-audio");
const manualPlayBtn = el("btn-manual-play");
const timerFillEl = el("timer-fill");

// ----- Utilitaires -----

function escapeHtml(str) {
  const div = document.createElement("div");
  div.textContent = str ?? "";
  return div.innerHTML;
}

function showScreen(id) {
  for (const s of screens) {
    el(s).classList.toggle("hidden", s !== id);
  }
}

function setConnected(connected) {
  connectionIndicator.textContent = connected ? "connecté" : "déconnecté";
  connectionIndicator.classList.toggle("pill--on", connected);
  connectionIndicator.classList.toggle("pill--off", !connected);
}

function nomJoueur(playerId) {
  const p = players.find((j) => j.playerId === playerId);
  return p ? p.nom : playerId;
}

function renderPlayers() {
  const list = el("lobby-players");
  list.innerHTML = "";
  for (const p of players) {
    const li = document.createElement("li");
    if (!p.estConnecte) li.classList.add("disconnected");
    li.innerHTML = `<span>${escapeHtml(p.nom)}</span><span>${p.estConnecte ? "" : "déconnecté"}</span>`;
    list.appendChild(li);
  }
}

function renderScoreList(container, dto) {
  container.innerHTML = "";

  const joueurs = [...dto.joueurs].sort((a, b) => b.score - a.score);
  for (const j of joueurs) {
    const li = document.createElement("li");
    li.innerHTML = `<span>${escapeHtml(j.nom)}</span><span>${j.score}</span>`;
    container.appendChild(li);
  }

  if (dto.equipes && dto.equipes.length > 0) {
    const header = document.createElement("li");
    header.innerHTML = "<strong>Équipes</strong><span></span>";
    container.appendChild(header);

    for (const eq of [...dto.equipes].sort((a, b) => b.score - a.score)) {
      const li = document.createElement("li");
      li.innerHTML = `<span>${escapeHtml(eq.nom)}</span><span>${eq.score}</span>`;
      container.appendChild(li);
    }
  }
}

function renderResults(resultats) {
  const body = el("results-body");
  body.innerHTML = "";

  for (const r of resultats) {
    const tr = document.createElement("tr");
    const reponseAffichee = r.reponse ? r.reponse : "(absent)";
    const classe = r.estCorrecte ? "correct" : "incorrect";
    tr.innerHTML = `
      <td>${escapeHtml(nomJoueur(r.playerId))}</td>
      <td>${escapeHtml(reponseAffichee)}</td>
      <td class="${classe}">${r.estCorrecte ? "oui" : "non"}</td>
      <td>${r.points}</td>
    `;
    body.appendChild(tr);
  }
}

// ----- Minuteur visuel (approximatif — le serveur reste seul juge du timing) -----

function startTimer(durationMs) {
  clearInterval(timerInterval);
  timerDurationMs = durationMs;
  timerEndAt = Date.now() + durationMs;
  timerPausedRemainingMs = null;
  timerInterval = setInterval(updateTimer, 100);
  updateTimer();
}

function updateTimer() {
  if (timerEndAt === null) return;
  const remaining = Math.max(0, timerEndAt - Date.now());
  const pct = timerDurationMs > 0 ? (remaining / timerDurationMs) * 100 : 0;
  timerFillEl.style.width = `${pct}%`;
  if (remaining <= 0) clearInterval(timerInterval);
}

function pauseTimer() {
  if (timerEndAt === null) return;
  timerPausedRemainingMs = Math.max(0, timerEndAt - Date.now());
  clearInterval(timerInterval);
}

function resumeTimer() {
  if (timerPausedRemainingMs === null) return;
  timerEndAt = Date.now() + timerPausedRemainingMs;
  timerPausedRemainingMs = null;
  timerInterval = setInterval(updateTimer, 100);
}

function stopTimer() {
  clearInterval(timerInterval);
  timerEndAt = null;
  timerPausedRemainingMs = null;
  timerFillEl.style.width = "0%";
}

// ----- Audio -----

function playAudio(filePath) {
  audioEl.src = `${serverBaseUrl}/files/${filePath}`;
  manualPlayBtn.classList.add("hidden");
  const playPromise = audioEl.play();
  if (playPromise && typeof playPromise.catch === "function") {
    playPromise.catch(() => manualPlayBtn.classList.remove("hidden"));
  }
}

manualPlayBtn.addEventListener("click", () => {
  audioEl.play();
  manualPlayBtn.classList.add("hidden");
});

// ----- Handlers SignalR -----

function registerHandlers() {
  connection.onclose(() => setConnected(false));
  connection.onreconnecting(() => setConnected(false));
  connection.onreconnected(() => setConnected(true));

  connection.on("PlayerJoined", ({ playerId, nom }) => {
    players.push({ playerId, nom, estConnecte: true });
    renderPlayers();
  });

  connection.on("PlayerReconnected", ({ playerId }) => {
    const p = players.find((j) => j.playerId === playerId);
    if (p) p.estConnecte = true;
    renderPlayers();
  });

  connection.on("PlayerDisconnected", ({ playerId }) => {
    const p = players.find((j) => j.playerId === playerId);
    if (p) p.estConnecte = false;
    renderPlayers();
  });

  connection.on("RoundStarted", (payload) => {
    el("round-error").textContent = "";
    el("round-mode-label").textContent = payload.mode;
    el("btn-pause").classList.remove("hidden");
    el("btn-resume").classList.add("hidden");
    showScreen("screen-round");
    playAudio(payload.filePath);
    startTimer(payload.dureeFenetreReponseMs);
  });

  connection.on("ScoreUpdate", (dto) => {
    renderScoreList(el("round-scores"), dto);
  });

  connection.on("RoundEnded", (payload) => {
    stopTimer();
    audioEl.pause();
    el("reveal-title").textContent = payload.title;
    el("reveal-artist").textContent = payload.artist;
    el("round-ended-note").textContent = "";
    renderResults(payload.resultats);
    showScreen("screen-round-ended");
  });

  connection.on("GamePaused", () => {
    audioEl.pause();
    pauseTimer();
    el("btn-pause").classList.add("hidden");
    el("btn-resume").classList.remove("hidden");
  });

  connection.on("GameResumed", () => {
    audioEl.play().catch(() => manualPlayBtn.classList.remove("hidden"));
    resumeTimer();
    el("btn-pause").classList.remove("hidden");
    el("btn-resume").classList.add("hidden");
  });

  connection.on("LeaderboardShown", (dto) => {
    renderScoreList(el("leaderboard-scores"), dto);
    el("leaderboard-overlay").classList.remove("hidden");
  });

  connection.on("GameEnded", (dto) => {
    stopTimer();
    audioEl.pause();
    renderScoreList(el("final-scores"), dto);
    showScreen("screen-ended");
  });
}

// ----- Connexion -----

el("btn-connect").addEventListener("click", async () => {
  const errorEl = el("connect-error");
  errorEl.textContent = "";

  let url = el("server-url").value.trim();
  if (!url) {
    errorEl.textContent = "Adresse requise (ex. http://192.168.1.42:5000).";
    return;
  }
  url = url.replace(/\/+$/, "");
  serverBaseUrl = url;

  connection = new signalR.HubConnectionBuilder()
    .withUrl(`${serverBaseUrl}/hubs/game`)
    .withAutomaticReconnect()
    .build();

  registerHandlers();

  try {
    await connection.start();
    setConnected(true);
    showScreen("screen-setup");
  } catch (err) {
    console.error(err);
    errorEl.textContent = "Connexion impossible : vérifiez l'adresse et que le serveur tourne.";
  }
});

// ----- Création de partie -----

function buildCreateGameRequest() {
  const tags = el("setup-tags")
    .value.split(",")
    .map((s) => s.trim())
    .filter(Boolean);

  const nombreRounds = parseInt(el("setup-nombre-rounds").value, 10);
  const dureeFenetreMs = parseInt(el("setup-duree-fenetre").value, 10) * 1000;
  const mode = el("setup-mode").value;
  const modeEquipe = el("setup-mode-equipe").checked;

  // Valeurs par défaut pour les paramètres non exposés dans ce premier écran de création
  // (à affiner plus tard) — restent configurables par partie via ce payload, jamais en dur côté serveur.
  const seriesConfig = {
    nombreRoundsClassiques: nombreRounds,
    dureeFenetreReponseMs: dureeFenetreMs,
    pointsMax: 100,
    pointsMin: 20,
    penaliteMauvaiseReponseRatio: 0.5,
    penaliteAbsenceReponse: -5,
    paliersDeMise: [10, 20, 30, 50],
    dureePhaseMiseMs: 15000,
    dureePhaseQuestionMs: 20000,
  };

  const roundModes = Array.from({ length: nombreRounds }, () => mode);

  return {
    tags,
    modeEquipe,
    seriesSetups: [{ config: seriesConfig, roundModes }],
    config: null,
  };
}

el("btn-create-game").addEventListener("click", async () => {
  const errorEl = el("setup-error");
  errorEl.textContent = "";

  try {
    const payload = buildCreateGameRequest();
    const result = await connection.invoke("CreateGame", payload);
    gameCode = result.code;
    players = [];
    el("lobby-code").textContent = gameCode;
    renderPlayers();
    showScreen("screen-lobby");
  } catch (err) {
    console.error(err);
    errorEl.textContent = "Erreur : " + (err.message || err);
  }
});

// ----- Lobby / round -----

el("btn-start-round").addEventListener("click", async () => {
  el("lobby-error").textContent = "";
  try {
    await connection.invoke("StartRound");
  } catch (err) {
    console.error(err);
    el("lobby-error").textContent = "Erreur : " + (err.message || err);
  }
});

el("btn-pause").addEventListener("click", async () => {
  try {
    await connection.invoke("PauseGame");
  } catch (err) {
    console.error(err);
    el("round-error").textContent = "Erreur : " + (err.message || err);
  }
});

el("btn-resume").addEventListener("click", async () => {
  try {
    await connection.invoke("ResumeGame");
  } catch (err) {
    console.error(err);
    el("round-error").textContent = "Erreur : " + (err.message || err);
  }
});

el("btn-leaderboard").addEventListener("click", async () => {
  try {
    await connection.invoke("ShowLeaderboard");
  } catch (err) {
    console.error(err);
    el("round-error").textContent = "Erreur : " + (err.message || err);
  }
});

el("btn-close-leaderboard").addEventListener("click", () => {
  el("leaderboard-overlay").classList.add("hidden");
});

el("btn-next-round").addEventListener("click", async () => {
  el("round-ended-note").textContent = "";
  try {
    await connection.invoke("NextRound");
    await connection.invoke("StartRound");
  } catch (err) {
    console.error(err);
    el("round-ended-note").textContent =
      "Plus de round classique dans cette série — cliquez sur \"Terminer la partie\" (la question bonus n'est pas encore gérée par cette page).";
  }
});

el("btn-end-game").addEventListener("click", async () => {
  try {
    await connection.invoke("EndGame");
  } catch (err) {
    console.error(err);
    el("round-ended-note").textContent = "Erreur : " + (err.message || err);
  }
});

showScreen("screen-connect");
