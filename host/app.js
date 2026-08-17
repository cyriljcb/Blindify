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

let autoNextTimeoutId = null;
let autoNextIntervalId = null;

let autoEndTimeoutId = null;
let autoEndIntervalId = null;

let refrainCourantMs = null;
let nombreRoundsTotal = 0;
let roundsDemarres = 0;

let dernierModeRound = null;
let dernierResultats = [];

// ----- Éléments DOM -----

const el = (id) => document.getElementById(id);

const connectionIndicator = el("connection-indicator");
const screens = [
  "screen-connect",
  "screen-setup",
  "screen-lobby",
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

const audioEl = el("player-audio");
const manualPlayBtn = el("btn-manual-play");
const gameControlsEl = el("game-controls");
let timerFillEl = null; // assigné dynamiquement selon la phase (round, mise bonus, question bonus)

// ----- Utilitaires -----

function escapeHtml(str) {
  const div = document.createElement("div");
  div.textContent = str ?? "";
  return div.innerHTML;
}

const DUREE_TRANSITION_MS = 350;

function showScreen(id) {
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
      <td></td>
    `;

    // Override manuel : utile pour les réponses tapées ambiguës (tolérance Levenshtein pas
    // toujours adaptée) — inutile pour un QCM (choix strict) ou une première lettre (déjà
    // binaire). Le morceau doit avoir été répondu (pas de mise à jour possible sinon).
    if (dernierModeRound === "TapeReponse" && r.reponse) {
      const cellOverride = tr.lastElementChild;
      const btnOui = document.createElement("button");
      btnOui.className = "btn-override btn-override--oui";
      btnOui.textContent = "✓";
      btnOui.title = "Marquer correct";
      btnOui.addEventListener("click", () => validerManuellement(r.playerId, true));

      const btnNon = document.createElement("button");
      btnNon.className = "btn-override btn-override--non";
      btnNon.textContent = "✗";
      btnNon.title = "Marquer incorrect";
      btnNon.addEventListener("click", () => validerManuellement(r.playerId, false));

      cellOverride.appendChild(btnOui);
      cellOverride.appendChild(btnNon);
    }

    body.appendChild(tr);
  }
}

async function validerManuellement(playerId, estCorrecte) {
  let resultat;
  try {
    resultat = await connection.invoke("ValidateAnswerManually", { playerId, estCorrecte });
  } catch (err) {
    console.error(err);
    el("round-ended-note").textContent = "Erreur override : " + (err.message || err);
    return;
  }

  // ScoreUpdate (score total) est déjà géré par ailleurs — on met à jour localement la
  // ligne concernée (statut + points de CE round) pour refléter le résultat sans attendre.
  const entree = dernierResultats.find((r) => r.playerId === playerId);
  if (entree) {
    entree.estCorrecte = resultat.estCorrecte;
    entree.points = resultat.points;
    renderResults(dernierResultats);
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

function renderBonusResults(resultats) {
  const body = el("bonus-results-body");
  body.innerHTML = "";

  for (const r of resultats) {
    const tr = document.createElement("tr");
    const reponseAffichee = r.reponse ? r.reponse : "(absent)";
    const classe = r.estCorrecte ? "correct" : "incorrect";
    tr.innerHTML = `
      <td>${escapeHtml(nomJoueur(r.playerId))}</td>
      <td>${r.mise}</td>
      <td>${escapeHtml(reponseAffichee)}</td>
      <td class="${classe}">${r.estCorrecte ? "oui" : "non"}</td>
      <td>${r.points}</td>
    `;
    body.appendChild(tr);
  }
}

// ----- Minuteur visuel (approximatif — le serveur reste seul juge du timing) -----

function startTimer(durationMs, fillEl) {
  clearInterval(timerInterval);
  timerFillEl = fillEl;
  timerDurationMs = durationMs;
  timerEndAt = Date.now() + durationMs;
  timerPausedRemainingMs = null;
  timerInterval = setInterval(updateTimer, 100);
  updateTimer();
}

function updateTimer() {
  if (timerEndAt === null || !timerFillEl) return;
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
  if (timerFillEl) timerFillEl.style.width = "0%";
}

// ----- Audio -----

let fadeIntervalId = null;

// Fondu de volume — évite les coupures sèches entre la découverte et le reveal, ou entre
// deux morceaux qui s'enchaînent automatiquement.
function fadeAudioVolume(cible, dureeMs, onDone) {
  clearInterval(fadeIntervalId);
  const depart = audioEl.volume;
  const debutTs = performance.now();
  fadeIntervalId = setInterval(() => {
    const t = Math.min(1, (performance.now() - debutTs) / dureeMs);
    audioEl.volume = depart + (cible - depart) * t;
    if (t >= 1) {
      clearInterval(fadeIntervalId);
      fadeIntervalId = null;
      if (onDone) onDone();
    }
  }, 30);
}

function lancerLecture() {
  const playPromise = audioEl.play();
  if (playPromise && typeof playPromise.catch === "function") {
    playPromise.catch(() => manualPlayBtn.classList.remove("hidden"));
  }
}

function playAudio(filePath) {
  audioEl.src = `${serverBaseUrl}/files/${filePath}`;
  // Force un rechargement propre même si l'URL est identique au morceau précédent (le
  // catalogue étant petit, "Rejouer" retombe facilement sur le même fichier) — sans ça,
  // certains navigateurs ne redéclenchent pas leurs événements de chargement et la lecture
  // reste bloquée sur l'état du round précédent.
  audioEl.load();
  manualPlayBtn.classList.add("hidden");
  audioEl.volume = 0;
  lancerLecture();
  fadeAudioVolume(1, 450);
}

// Au reveal (round classique ou question bonus) : petit fondu avant de sauter au refrain,
// pour éviter la coupure sèche entre la découverte et la révélation.
function jouerRefrain(refrainStartMs) {
  fadeAudioVolume(0, 280, () => {
    audioEl.currentTime = refrainStartMs / 1000;
    lancerLecture();
    fadeAudioVolume(1, 450);
  });
}

function pauseAudioEnDouceur() {
  fadeAudioVolume(0, 320, () => audioEl.pause());
}

manualPlayBtn.addEventListener("click", () => {
  audioEl.play();
  manualPlayBtn.classList.add("hidden");
});

// ----- Handlers SignalR -----

function registerHandlers() {
  connection.onclose((err) => {
    console.error("SignalR onclose:", err);
    setConnected(false);
  });
  connection.onreconnecting((err) => {
    console.warn("SignalR onreconnecting:", err);
    setConnected(false);
  });
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
    const cibleLabel = payload.cible === "Titre" ? "le titre" : "l'artiste";
    el("round-mode-label").textContent = `${payload.mode} — trouver ${cibleLabel}`;
    dernierModeRound = payload.mode;
    el("btn-pause").classList.remove("hidden");
    el("btn-resume").classList.add("hidden");
    showScreen("screen-round");
    refrainCourantMs = payload.refrainStartMs ?? null;
    playAudio(payload.filePath); // toujours depuis le début pendant la découverte — le refrain n'est joué qu'au reveal
    startTimer(payload.dureeFenetreReponseMs, el("timer-fill"));
    roundsDemarres++;
  });

  connection.on("ScoreUpdate", (dto) => {
    renderScoreList(el("round-scores"), dto);
  });

  connection.on("RoundEnded", (payload) => {
    stopTimer();
    // Au reveal (tout le monde a répondu) : on saute au refrain si on en connaît un pour ce
    // morceau, sinon on retombe sur le comportement "musique continue" habituel.
    if (refrainCourantMs !== null) {
      jouerRefrain(refrainCourantMs);
    } else if (!el("setup-audio-continu").checked) {
      pauseAudioEnDouceur();
    }
    el("reveal-title").textContent = payload.title;
    el("reveal-artist").textContent = payload.artist;
    el("round-ended-note").textContent = "";
    dernierResultats = payload.resultats;
    renderResults(dernierResultats);
    showScreen("screen-round-ended");
    scheduleAutoNext();
  });

  connection.on("GamePaused", () => {
    pauseAudioEnDouceur();
    pauseTimer();
    cancelAutoNext();
    cancelAutoEnd();
    el("btn-pause").classList.add("hidden");
    el("btn-resume").classList.remove("hidden");
  });

  connection.on("GameResumed", () => {
    lancerLecture();
    fadeAudioVolume(1, 450);
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
    cancelAutoEnd();
    audioEl.pause();
    audioEl.playbackRate = 1;
    renderScoreList(el("final-scores"), dto);
    showScreen("screen-ended");
  });

  connection.on("GameRestarted", () => {
    roundsDemarres = 0;
    audioEl.playbackRate = 1;
    el("lobby-code").textContent = gameCode;
    renderPlayers();
    showScreen("screen-lobby");
  });

  connection.on("BonusStakeOptions", (payload) => {
    renderBonusPaliers(payload.paliers);
    showScreen("screen-bonus-stake");
    startTimer(payload.dureePhaseMiseMs, el("bonus-stake-timer-fill"));
  });

  connection.on("BonusQuestionStarted", (payload) => {
    // Seul le host reçoit filePath/refrainStartMs/ralentissement (jamais envoyés aux joueurs).
    refrainCourantMs = payload.refrainStartMs ?? null;
    audioEl.playbackRate = payload.ralentissementActive ? payload.facteurRalentissement : 1;
    playAudio(payload.filePath); // depuis le début — c'est la devinette elle-même, pas le reveal
    showScreen("screen-bonus-question");
    startTimer(payload.dureePhaseQuestionMs, el("bonus-question-timer-fill"));
  });

  connection.on("BonusResult", (payload) => {
    stopTimer();
    audioEl.playbackRate = 1; // remis à la vitesse normale pour la suite (fin de partie, replay...)
    if (refrainCourantMs !== null) {
      jouerRefrain(refrainCourantMs);
    } else {
      pauseAudioEnDouceur();
    }
    el("bonus-reveal-title").textContent = payload.title;
    el("bonus-reveal-artist").textContent = payload.artist;
    renderBonusResults(payload.resultats);
    showScreen("screen-bonus-result");
    scheduleAutoEndGame();
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
    .withUrl(`${serverBaseUrl}/hubs/game`, { withCredentials: false })
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

const ROUND_MODES = ["Qcm", "TapeReponse", "PremiereLettre"];

// Tiré aléatoirement par round plutôt que configuré manuellement — le contrat
// backend (roundModes) accepte un mode par round, la génération est laissée au code.
function pickRandomRoundModes(count) {
  return Array.from({ length: count }, () => ROUND_MODES[Math.floor(Math.random() * ROUND_MODES.length)]);
}

function buildCreateGameRequest() {
  const tags = el("setup-tags")
    .value.split(",")
    .map((s) => s.trim())
    .filter(Boolean);

  const nombreRounds = parseInt(el("setup-nombre-rounds").value, 10);
  const dureeFenetreMs = parseInt(el("setup-duree-fenetre").value, 10) * 1000;
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

  const roundModes = pickRandomRoundModes(nombreRounds);

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
    nombreRoundsTotal = payload.seriesSetups[0].config.nombreRoundsClassiques;
    roundsDemarres = 0;
    players = [];
    el("lobby-code").textContent = gameCode;
    renderPlayers();
    showScreen("screen-lobby");
  } catch (err) {
    console.error(err);
    errorEl.textContent = "Erreur : " + (err.message || err);
  }
});

// ----- Enchaînement automatique des rounds -----

function cancelAutoNext() {
  if (autoNextTimeoutId !== null) {
    clearTimeout(autoNextTimeoutId);
    autoNextTimeoutId = null;
  }
  if (autoNextIntervalId !== null) {
    clearInterval(autoNextIntervalId);
    autoNextIntervalId = null;
  }
  el("auto-next-note").textContent = "";
}

async function avancerRoundSuivant() {
  el("round-ended-note").textContent = "";

  // Vérifié côté client avant d'appeler le serveur : la série classique est épuisée, on
  // enchaîne directement sur la question bonus plutôt que d'attendre une intervention.
  if (roundsDemarres >= nombreRoundsTotal) {
    try {
      await connection.invoke("StartBonusRound");
    } catch (err) {
      console.error(err);
      el("round-ended-note").textContent = "Erreur au démarrage de la question bonus : " + (err.message || err);
    }
    return;
  }

  try {
    await connection.invoke("NextRound");
    await connection.invoke("StartRound");
  } catch (err) {
    console.error(err);
    el("round-ended-note").textContent = "Erreur : " + (err.message || err);
  }
}

function scheduleAutoNext() {
  cancelAutoNext();

  if (roundsDemarres >= nombreRoundsTotal) {
    avancerRoundSuivant(); // enchaîne directement sur la question bonus, sans décompte inutile
    return;
  }

  const delaiMs = Math.max(1, parseInt(el("setup-delai-enchainement").value, 10) || 6) * 1000;
  const finA = Date.now() + delaiMs;

  const updateNote = () => {
    const restant = Math.max(0, Math.ceil((finA - Date.now()) / 1000));
    el("auto-next-note").textContent = `Round suivant dans ${restant}s...`;
  };
  updateNote();
  autoNextIntervalId = setInterval(updateNote, 250);

  autoNextTimeoutId = setTimeout(() => {
    cancelAutoNext();
    avancerRoundSuivant();
  }, delaiMs);
}

// ----- Fin de partie automatique (après le résultat de la question bonus) -----

function cancelAutoEnd() {
  if (autoEndTimeoutId !== null) {
    clearTimeout(autoEndTimeoutId);
    autoEndTimeoutId = null;
  }
  if (autoEndIntervalId !== null) {
    clearInterval(autoEndIntervalId);
    autoEndIntervalId = null;
  }
  el("bonus-end-note").textContent = "";
}

function scheduleAutoEndGame() {
  cancelAutoEnd();

  const delaiMs = Math.max(1, parseInt(el("setup-delai-enchainement").value, 10) || 6) * 1000;
  const finA = Date.now() + delaiMs;

  const updateNote = () => {
    const restant = Math.max(0, Math.ceil((finA - Date.now()) / 1000));
    el("bonus-end-note").textContent = `Fin de la partie dans ${restant}s...`;
  };
  updateNote();
  autoEndIntervalId = setInterval(updateNote, 250);

  autoEndTimeoutId = setTimeout(async () => {
    cancelAutoEnd();
    try {
      await connection.invoke("EndGame");
    } catch (err) {
      console.error(err);
      el("bonus-end-note").textContent = "Erreur : " + (err.message || err);
    }
  }, delaiMs);
}

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
  cancelAutoNext();
  cancelAutoEnd();
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

el("btn-next-round").addEventListener("click", () => {
  cancelAutoNext();
  avancerRoundSuivant();
});

el("btn-end-game").addEventListener("click", async () => {
  cancelAutoNext();
  try {
    await connection.invoke("EndGame");
  } catch (err) {
    console.error(err);
    el("round-ended-note").textContent = "Erreur : " + (err.message || err);
  }
});

el("btn-end-now").addEventListener("click", async () => {
  cancelAutoEnd();
  try {
    await connection.invoke("EndGame");
  } catch (err) {
    console.error(err);
    el("bonus-end-note").textContent = "Erreur : " + (err.message || err);
  }
});

el("btn-rejouer").addEventListener("click", async () => {
  el("ended-error").textContent = "";
  try {
    await connection.invoke("RejouerPartie");
  } catch (err) {
    console.error(err);
    el("ended-error").textContent = "Erreur : " + (err.message || err);
  }
});

showScreen("screen-connect");
