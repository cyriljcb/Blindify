"use strict";

// ----- État global -----

let connection = null;
let serverBaseUrl = "";
let gameCode = null;
// Distinct du code de partie (public) — requis par RejoinAsHost, jamais envoyé aux joueurs.
// Conservé ici pour une future reprise après refresh/crash de l'onglet (RejoinAsHost n'est pas
// encore invoqué automatiquement par ce panneau).
let hostSecret = null;
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
let nombreRoundsParSerie = 0; // identique pour chaque série (champ partagé, voir buildConfigurerPartieRequest)
let nombreSeriesTotal = 0;
let serieCouranteIndex = 0;

// Retour utilisateur du 2026-08-24 : CreateGame ne configure plus le blindtest (voir ConfigurerPartie) —
// "Démarrer la partie" reste désactivé tant qu'une configuration n'a pas été validée avec succès au
// moins une fois pour CE lobby.
let partieConfiguree = false;
let roundsDemarres = 0; // rounds démarrés DANS LA SÉRIE COURANTE — remis à 0 à chaque changement de série
let tagsDisponibles = []; // thèmes connus du catalogue (GET /api/tags), pour les cases à cocher

let dernierModeRound = null;
let dernierResultats = [];

let equipes = []; // [{ id, nom }] — vide si mode équipe inactif
let equipeParJoueur = {}; // playerId -> teamId

// ----- Historique des scores (graphique de fin de partie) -----
// Un seul point par SÉRIE résolue (capturé à la question bonus, qui marque la fin de série) —
// retour utilisateur : pas un point par round, trop granulaire pour "qui menait quand". Jamais un
// point par réponse individuelle non plus (ScoreUpdate est diffusé à chaque soumission).
let scoreHistory = [];
let dernierScoreDto = null;
let historiqueLabelEnAttente = null; // label du prochain ScoreUpdate à enregistrer, voir plus bas

// ----- Écran public (fenêtre séparée, même PC — voir diffusion plus bas) -----

let displayWindow = null;
let jeuEnPause = false;
let currentDisplayScreen = "idle";
let currentRoundInfo = {};
let currentRevealInfo = {};
let currentBonusInfo = {};
let currentScoresInfo = null;
let currentSerieIntroInfo = {};
let lastDisplayState = null;
let leaderboardOpen = false;
let lastLeaderboardDto = null;

// ----- Éléments DOM -----

const el = (id) => document.getElementById(id);

const connectionIndicator = el("connection-indicator");
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

// Cible Film (morceaux "disney", voir RoundService.DemarrerRound) : la réponse attendue était le
// film, pas le titre réel de la chanson — le reveal doit donc mettre le film en avant. Le vrai
// titre/artiste reste affiché en dessous, à titre de bonus trivia.
function libelleReveal(payload) {
  if (payload.cible === "Film") {
    return { titre: payload.film, sousTitre: `${payload.title} — ${payload.artist}` };
  }
  return { titre: payload.title, sousTitre: payload.artist };
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

function nomEquipe(teamId) {
  const eq = equipes.find((e) => e.id === teamId);
  return eq ? eq.nom : null;
}

// Palette de couleurs manuellement choisies pour rester visuellement distinctes entre indices
// voisins (un hash%360 pouvait donner deux teintes de vert à deux joueurs différents — retour
// utilisateur). Identique dans host/display.js et app/lib/widgets/player_avatar.dart.
const PALETTE_AVATARS = [
  "#E63946", "#457B9D", "#F4A300", "#2A9D8F",
  "#9B5DE5", "#06D6A0", "#F15BB5", "#4CC9F0",
  "#FF6B35", "#8AC926", "#FFCA3A", "#6A4C93",
];

// Couleur stable par identifiant, basée sur la position dans le roster (ordre d'arrivée) plutôt
// que sur un hash de l'id, pour garantir des couleurs distinctes entre joueurs (et entre équipes)
// tant que leur nombre ne dépasse pas la taille de la palette.
function couleurAvatar(id) {
  const indexJoueur = players.findIndex((p) => p.playerId === id);
  if (indexJoueur >= 0) return PALETTE_AVATARS[indexJoueur % PALETTE_AVATARS.length];

  const indexEquipe = equipes.findIndex((e) => e.id === id);
  if (indexEquipe >= 0) return PALETTE_AVATARS[(players.length + indexEquipe) % PALETTE_AVATARS.length];

  let hash = 0;
  for (let i = 0; i < id.length; i++) hash = (hash * 31 + id.charCodeAt(i)) >>> 0;
  return `hsl(${hash % 360}, 65%, 55%)`;
}

const MEDAILLES = ["🥇", "🥈", "🥉"];

// rang (0-indexé) : si fourni et < 3, affiche une médaille à la place des initiales
// (classement final / tableau général uniquement — pas pendant un round en cours).
function avatarHtml(id, nom, rang) {
  if (rang !== undefined && rang < 3) {
    return `<span class="avatar" style="background: transparent; font-size: 1.3rem;">${MEDAILLES[rang]}</span>`;
  }
  const initiale = escapeHtml(nom.trim().slice(0, 1).toUpperCase() || "?");
  return `<span class="avatar" style="background: ${couleurAvatar(id)};">${initiale}</span>`;
}

function renderPlayers() {
  const list = el("lobby-players");
  list.innerHTML = "";
  for (const p of players) {
    const li = document.createElement("li");
    if (!p.estConnecte) li.classList.add("disconnected");
    const equipeJoueur = nomEquipe(equipeParJoueur[p.playerId]);
    const statut = [equipeJoueur, p.estConnecte ? "" : "déconnecté"].filter(Boolean).join(" — ");
    li.innerHTML = `
      <div class="player-row">${avatarHtml(p.playerId, p.nom)}<span>${escapeHtml(p.nom)}</span></div>
      <span>${escapeHtml(statut)}</span>
    `;
    list.appendChild(li);
  }
}

function renderScoreList(container, dto, { medailles = false } = {}) {
  container.innerHTML = "";

  const joueurs = [...dto.joueurs].sort((a, b) => b.score - a.score);
  joueurs.forEach((j, index) => {
    const li = document.createElement("li");
    li.innerHTML = `
      <div class="player-row">${avatarHtml(j.playerId, j.nom, medailles ? index : undefined)}<span>${escapeHtml(j.nom)}</span></div>
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
        <div class="player-row">${avatarHtml(eq.teamId, eq.nom, medailles ? index : undefined)}<span>${escapeHtml(eq.nom)}</span></div>
        <span>${eq.score}</span>
      `;
      container.appendChild(li);
    });
  }
}

// Graphique "qui menait quand" — un point par série résolue (voir scoreHistory), plus un point de
// départ (tout le monde à 0) et le score final. Dessiné en SVG à la main (pas de bibliothèque
// externe, cohérent avec le reste de host/) — une ligne par joueur/équipe, couleur alignée sur les
// avatars utilisés partout ailleurs. Partagé avec l'écran public (voir display.js) : reçu via
// syncDisplay plutôt que redessiné indépendamment, pour garantir un rendu identique.
function renderScoreChart(container, history, finalDto) {
  container.innerHTML = "";
  if (history.length === 0) return; // partie terminée avant le moindre round résolu

  const useEquipes = finalDto.equipes && finalDto.equipes.length > 0;
  const depart = {
    label: "Départ",
    joueurs: players.map((p) => ({ playerId: p.playerId, score: 0 })),
    equipes: equipes.map((e) => ({ teamId: e.id, score: 0 })),
  };
  const points = [depart, ...history, { label: "Final", joueurs: finalDto.joueurs, equipes: finalDto.equipes }];
  const series = useEquipes
    ? finalDto.equipes.map((e) => ({ id: e.teamId, nom: e.nom }))
    : finalDto.joueurs.map((j) => ({ id: j.playerId, nom: j.nom }));

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
    const couleur = couleurAvatar(s.id);
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
      <span class="score-chart-legend-swatch" style="background: ${couleurAvatar(s.id)}"></span>
      ${escapeHtml(s.nom)}
    </span>`
    )
    .join("");

  container.innerHTML = `${svg}<div class="score-chart-legend">${legende}</div>`;
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

    if (currentRevealInfo.resultats) {
      const entreePublique = currentRevealInfo.resultats.find((r) => r.playerId === playerId);
      if (entreePublique) entreePublique.estCorrecte = resultat.estCorrecte;
      syncDisplay();
    }
  }
}

function afficherCover(imgEl, placeholderEl, coverPath) {
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
  timerFillEl.classList.toggle("timer-fill--warn", pct <= 40 && pct > 15);
  timerFillEl.classList.toggle("timer-fill--danger", pct <= 15);
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

function playAudio(filePath, playbackRate = 1) {
  const demarrerNouveauMorceau = () => {
    audioEl.src = `${serverBaseUrl}/files/${filePath}`;
    // Force un rechargement propre même si l'URL est identique au morceau précédent (le
    // catalogue étant petit, "Rejouer" retombe facilement sur le même fichier) — sans ça,
    // certains navigateurs ne redéclenchent pas leurs événements de chargement et la lecture
    // reste bloquée sur l'état du round précédent.
    audioEl.load();
    // playbackRate doit être réappliqué APRÈS load() : certains navigateurs le réinitialisent
    // à 1 au chargement, ce qui annulait silencieusement le ralentissement de la question bonus.
    audioEl.playbackRate = playbackRate;
    manualPlayBtn.classList.add("hidden");
    audioEl.volume = 0;
    lancerLecture();
    fadeAudioVolume(1, 450);
  };

  // Si un morceau est déjà en cours (typiquement le refrain du reveal qui vient de se terminer),
  // on le fait d'abord redescendre en silence avant de changer la source — sans ce fondu de
  // sortie, audioEl.load() coupe la lecture précédente instantanément et seule l'arrivée du
  // morceau suivant était fondue, donnant une impression de coupure sèche plutôt que de
  // transition (retour utilisateur : "aucune transition entre le reveal et la découverte").
  if (!audioEl.paused && !audioEl.ended) {
    fadeAudioVolume(0, 280, demarrerNouveauMorceau);
  } else {
    demarrerNouveauMorceau();
  }
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

// ----- Écran public : ouverture + diffusion d'état -----
//
// L'écran public tourne dans une fenêtre séparée du même navigateur (même PC, écran étendu/
// vidéoprojecteur — voir CLAUDE.md). On garde volontairement zéro dépendance au serveur pour ce
// canal : postMessage direct vers la référence de fenêtre obtenue via window.open, ce qui évite
// tout souci d'origine avec des pages ouvertes en file:// (BroadcastChannel exigerait une origine
// strictement identique, ce que file:// ne garantit pas d'un navigateur à l'autre).

const displayIndicatorEl = el("display-indicator");

function setDisplayIndicator(ouvert) {
  displayIndicatorEl.textContent = ouvert ? "écran public : ouvert" : "écran public : fermé";
  displayIndicatorEl.classList.toggle("pill--on", ouvert);
  displayIndicatorEl.classList.toggle("pill--off", !ouvert);
}

el("btn-open-display").addEventListener("click", () => {
  if (displayWindow && !displayWindow.closed) {
    displayWindow.focus();
    return;
  }
  displayWindow = window.open("display.html", "blindify-display", "width=1280,height=800");
  setDisplayIndicator(true);
});

setInterval(() => {
  if (displayWindow) setDisplayIndicator(!displayWindow.closed);
}, 1000);

window.addEventListener("message", (event) => {
  if (event.data?.type === "request-sync") {
    if (lastDisplayState) sendToDisplay(lastDisplayState);
    if (leaderboardOpen && lastLeaderboardDto) sendToDisplay({ type: "leaderboard-show", scores: lastLeaderboardDto });
    return;
  }

  // Bouton "Lancer" sur l'écran public (retour utilisateur : éviter le switch de fenêtre) — ne fait
  // rien si la partie n'est plus au lobby, exactement comme un second clic sur btn-start-round.
  if (event.data?.type === "start-round" && currentDisplayScreen === "lobby") {
    lancerLaPartieDepuisLobby();
  }
});

function sendToDisplay(msg) {
  if (displayWindow && !displayWindow.closed) displayWindow.postMessage(msg, "*");
}

// Rassemble l'état courant (jamais les infos sensibles : pas de filePath, pas de points/scores
// hors des moments de reveal explicitement voulus — tableau général, fin de partie).
function syncDisplay() {
  const msg = {
    type: "state",
    screen: currentDisplayScreen,
    gameCode,
    serverBaseUrl,
    paused: jeuEnPause,
    players: players.map((p) => ({ playerId: p.playerId, nom: p.nom, estConnecte: p.estConnecte })),
    equipeParJoueur,
    equipes,
    timerEndAt,
    timerDurationMs,
    round: currentRoundInfo,
    reveal: currentRevealInfo,
    bonus: currentBonusInfo,
    scores: currentScoresInfo,
    serieIntro: currentSerieIntroInfo,
    // Uniquement pertinent (et présent) au moment du GameEnded — voir renderScoreChart côté
    // display.js, partagé avec le panneau host pour un rendu identique du graphique.
    scoreHistory,
  };
  lastDisplayState = msg;
  sendToDisplay(msg);
}

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
    syncDisplay();
  });

  connection.on("PlayerReconnected", ({ playerId }) => {
    const p = players.find((j) => j.playerId === playerId);
    if (p) p.estConnecte = true;
    renderPlayers();
    syncDisplay();
  });

  connection.on("PlayerDisconnected", ({ playerId }) => {
    const p = players.find((j) => j.playerId === playerId);
    if (p) p.estConnecte = false;
    renderPlayers();
    syncDisplay();
  });

  connection.on("PlayerTeamChanged", ({ playerId, teamId }) => {
    equipeParJoueur[playerId] = teamId;
    renderPlayers();
    syncDisplay();
  });

  connection.on("RoundStarted", (payload) => {
    el("round-error").textContent = "";
    const cibleLabel = payload.cible === "Titre" ? "le titre" : payload.cible === "Auteur" ? "l'artiste" : "le film";
    el("round-mode-label").textContent = `${payload.mode} — trouver ${cibleLabel}${libelleSerie(serieCouranteIndex)}`;
    dernierModeRound = payload.mode;
    el("btn-pause").classList.remove("hidden");
    el("btn-resume").classList.add("hidden");
    showScreen("screen-round");
    refrainCourantMs = payload.refrainStartMs ?? null;
    playAudio(payload.filePath); // toujours depuis le début pendant la découverte — le refrain n'est joué qu'au reveal
    startTimer(payload.dureeFenetreReponseMs, el("timer-fill"));
    roundsDemarres++;

    currentDisplayScreen = "round";
    // qcmOptions : retour utilisateur — affiche aussi les choix sur l'écran public en mode QCM
    // (undefined pour les autres modes, voir display.js:renderQcmOptionsDisplay).
    currentRoundInfo = {
      mode: payload.mode,
      cible: payload.cible,
      serieLabel: libelleSerie(serieCouranteIndex),
      qcmOptions: payload.qcmOptions,
    };
    syncDisplay();
  });

  connection.on("ScoreUpdate", (dto) => {
    renderScoreList(el("round-scores"), dto);
    // Jamais diffusé à l'écran public : les scores en continu ne doivent pas être visibles des
    // joueurs pendant la partie (voir header.control-note) — seuls le tableau général et la fin
    // de partie révèlent volontairement les scores.

    // Mémorisé pour le graphique de fin de partie (voir RoundEnded/BonusResult) — ScoreUpdate est
    // diffusé à chaque réponse individuelle. Capturé pour le point d'historique en attente s'il y
    // en a un — RoundEnded/BonusResult sont TOUJOURS suivis d'un ScoreUpdate côté serveur (jamais
    // l'inverse, voir RoundTimerCoordinator/BonusTimerCoordinator), donc c'est ici, sur ce
    // ScoreUpdate qui suit, qu'il faut enregistrer le score à jour — pas au moment de
    // RoundEnded/BonusResult eux-mêmes, qui arrivent avant et portent encore l'ancien score.
    dernierScoreDto = dto;
    if (historiqueLabelEnAttente) {
      scoreHistory.push({ label: historiqueLabelEnAttente, joueurs: dto.joueurs, equipes: dto.equipes });
      historiqueLabelEnAttente = null;
    }
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
    const { titre, sousTitre } = libelleReveal(payload);
    el("reveal-title").textContent = titre;
    el("reveal-artist").textContent = sousTitre;
    afficherCover(el("reveal-cover"), el("reveal-cover-placeholder"), payload.coverPath);
    el("round-ended-note").textContent = "";
    dernierResultats = payload.resultats;
    renderResults(dernierResultats);
    showScreen("screen-round-ended");
    scheduleAutoNext();

    currentDisplayScreen = "round-ended";
    currentRevealInfo = {
      title: payload.title,
      artist: payload.artist,
      cible: payload.cible,
      film: payload.film,
      coverPath: payload.coverPath,
      resultats: dernierResultats.map((r) => ({ playerId: r.playerId, estCorrecte: r.estCorrecte })),
    };
    syncDisplay();
  });

  connection.on("GamePaused", () => {
    pauseAudioEnDouceur();
    pauseTimer();
    cancelAutoNext();
    cancelAutoEnd();
    el("btn-pause").classList.add("hidden");
    el("btn-resume").classList.remove("hidden");

    jeuEnPause = true;
    syncDisplay();
  });

  connection.on("GameResumed", () => {
    lancerLecture();
    fadeAudioVolume(1, 450);
    resumeTimer();
    el("btn-pause").classList.remove("hidden");
    el("btn-resume").classList.add("hidden");

    jeuEnPause = false;
    syncDisplay(); // timerEndAt a été recalculé par resumeTimer()
  });

  connection.on("LeaderboardShown", (dto) => {
    renderScoreList(el("leaderboard-scores"), dto, { medailles: true });
    el("leaderboard-overlay").classList.remove("hidden");

    leaderboardOpen = true;
    lastLeaderboardDto = dto;
    sendToDisplay({ type: "leaderboard-show", scores: dto });
  });

  connection.on("GameEnded", (dto) => {
    stopTimer();
    cancelAutoEnd();
    // Musique laissée telle quelle (retour utilisateur : garder l'ambiance) — seul EndGame
    // manuel/pause l'arrêtent explicitement, pas la fin automatique.
    renderScoreList(el("final-scores"), dto, { medailles: true });
    renderScoreChart(el("score-chart"), scoreHistory, dto);
    showScreen("screen-ended");

    currentDisplayScreen = "ended";
    currentScoresInfo = dto;
    syncDisplay();
  });

  connection.on("GameRestarted", () => {
    cancelAutoIntroSerie();
    roundsDemarres = 0;
    serieCouranteIndex = 0;
    scoreHistory = [];
    dernierScoreDto = null;
    historiqueLabelEnAttente = null;
    audioEl.playbackRate = 1;
    // RejouerPartie (backend) reconstruit SeriesList à partir de la configuration précédente (mêmes
    // séries/tags, nouvelle sélection de morceaux) — la partie reste donc immédiatement démarrable,
    // pas besoin de repasser par ConfigurerPartie.
    partieConfiguree = true;
    el("btn-start-round").disabled = false;
    el("lobby-code").textContent = gameCode;
    renderPlayers();
    showScreen("screen-lobby");

    currentDisplayScreen = "lobby";
    currentRoundInfo = {};
    currentRevealInfo = {};
    currentScoresInfo = null;
    syncDisplay();
  });

  connection.on("BonusStakeOptions", (payload) => {
    el("bonus-stake-title").textContent = `Question bonus — mise à l'aveugle${libelleSerie(serieCouranteIndex)}`;
    renderBonusPaliers(payload.paliers);
    showScreen("screen-bonus-stake");
    startTimer(payload.dureePhaseMiseMs, el("bonus-stake-timer-fill"));

    currentDisplayScreen = "bonus-stake";
    currentBonusInfo = { paliers: payload.paliers, serieLabel: libelleSerie(serieCouranteIndex) };
    syncDisplay();
  });

  connection.on("BonusQuestionStarted", (payload) => {
    // Seul le host reçoit filePath/refrainStartMs/ralentissement (jamais envoyés aux joueurs).
    refrainCourantMs = payload.refrainStartMs ?? null;
    const rate = payload.ralentissementActive ? payload.facteurRalentissement : 1;
    playAudio(payload.filePath, rate); // depuis le début — c'est la devinette elle-même, pas le reveal
    el("bonus-question-title").textContent = `Question bonus — à deviner !${libelleSerie(serieCouranteIndex)}`;
    const cibleLabelBonus = payload.cible === "Titre" ? "le titre" : payload.cible === "Auteur" ? "l'artiste" : "le film";
    el("bonus-question-mode-label").textContent = `${payload.mode} — trouver ${cibleLabelBonus}`;
    showScreen("screen-bonus-question");
    startTimer(payload.dureePhaseQuestionMs, el("bonus-question-timer-fill"));

    currentDisplayScreen = "bonus-question";
    // cible/qcmOptions : retour utilisateur — Mode tiré aléatoirement comme un round classique
    // (QCM/Première lettre en plus de la réponse tapée), qcmOptions affiché sur l'écran public en
    // mode QCM comme pour un round classique (voir display.js:renderQcmOptionsDisplay).
    currentBonusInfo = {
      ralenti: payload.ralentissementActive,
      serieLabel: libelleSerie(serieCouranteIndex),
      cible: payload.cible,
      qcmOptions: payload.qcmOptions,
    };
    syncDisplay();
  });

  connection.on("BonusResult", (payload) => {
    stopTimer();
    audioEl.playbackRate = 1; // remis à la vitesse normale pour la suite (fin de partie, replay...)
    if (refrainCourantMs !== null) {
      jouerRefrain(refrainCourantMs);
    } else {
      pauseAudioEnDouceur();
    }
    const { titre, sousTitre } = libelleReveal(payload);
    el("bonus-reveal-title").textContent = titre;
    el("bonus-reveal-artist").textContent = sousTitre;
    afficherCover(el("bonus-reveal-cover"), el("bonus-reveal-cover-placeholder"), payload.coverPath);
    renderBonusResults(payload.resultats);
    // Capturé AVANT l'incrément de serieCouranteIndex plus bas : ce résultat concerne la série qui
    // vient de se terminer, pas la suivante — currentRevealInfo (diffusé à l'écran public) doit
    // recevoir le même libellé que le titre affiché ici côté host.
    const labelSerieTerminee = libelleSerie(serieCouranteIndex);
    el("bonus-result-title").textContent = `Résultat de la question bonus${labelSerieTerminee}`;
    showScreen("screen-bonus-result");

    // Un seul point par série (pas un par round) : retour utilisateur — la question bonus marque
    // la fin de la série, c'est le seul moment où on capture le score sur le graphique.
    historiqueLabelEnAttente = `Série ${lettreSerie(serieCouranteIndex)}`;

    // Question bonus = fin de la série courante — enchaîne sur la série suivante s'il en reste
    // une, sinon termine la partie (comportement historique, valable quand il n'y a qu'une série).
    // Le bouton manuel (btn-end-now) doit refléter la même alternative, sinon "Terminer maintenant"
    // mettrait fin à la partie en sautant les séries restantes.
    serieCouranteIndex++;
    const seriesRestantes = serieCouranteIndex < nombreSeriesTotal;
    el("btn-end-now").textContent = seriesRestantes
      ? `Série suivante maintenant${libelleSerie(serieCouranteIndex)}`
      : "Terminer maintenant";
    if (seriesRestantes) {
      scheduleAutoNextSerie();
    } else {
      scheduleAutoEndGame();
    }

    currentDisplayScreen = "bonus-result";
    currentRevealInfo = {
      title: payload.title,
      artist: payload.artist,
      cible: payload.cible,
      film: payload.film,
      coverPath: payload.coverPath,
      serieLabel: labelSerieTerminee,
      resultats: payload.resultats.map((r) => ({ playerId: r.playerId, estCorrecte: r.estCorrecte })),
    };
    syncDisplay();
  });
}

// ----- Connexion -----

const SERVER_URL_STORAGE_KEY = "blindify_host_server_url";

// [timeoutMs] borne la tentative (utilisé pour la reconnexion auto au démarrage, voir plus bas) ;
// sans borne pour un clic manuel sur "Se connecter". [silencieux] masque le message d'erreur en
// cas d'échec — évite une alerte pour une tentative que l'utilisateur n'a pas déclenchée lui-même
// (même pattern que GameConnection.connect côté Flutter).
async function tenterConnexion(url, { silencieux = false, timeoutMs = null } = {}) {
  const errorEl = el("connect-error");
  if (!silencieux) errorEl.textContent = "";

  url = url.trim().replace(/\/+$/, "");
  if (!url) {
    if (!silencieux) errorEl.textContent = "Adresse requise (ex. http://192.168.1.42:5000).";
    return false;
  }
  serverBaseUrl = url;

  connection = new signalR.HubConnectionBuilder()
    .withUrl(`${serverBaseUrl}/hubs/game`, { withCredentials: false })
    .withAutomaticReconnect()
    .build();

  registerHandlers();

  try {
    const demarrage = connection.start();
    if (timeoutMs) {
      await Promise.race([
        demarrage,
        new Promise((_, reject) => setTimeout(() => reject(new Error("Délai de connexion dépassé")), timeoutMs)),
      ]);
    } else {
      await demarrage;
    }
    setConnected(true);
    localStorage.setItem(SERVER_URL_STORAGE_KEY, serverBaseUrl);
    showScreen("screen-setup");
    chargerTagsDisponibles(); // best-effort — n'empêche pas de créer une partie si ça échoue (voir plus bas)
    return true;
  } catch (err) {
    console.error(err);
    if (!silencieux) errorEl.textContent = "Connexion impossible : vérifiez l'adresse et que le serveur tourne.";
    showScreen("screen-connect");
    return false;
  }
}

el("btn-connect").addEventListener("click", () => tenterConnexion(el("server-url").value));

// ----- Création de partie -----

// Récupère les thèmes existants dans le catalogue (endpoint REST, pas SignalR — pas besoin d'une
// connexion de jeu pour lister des tags) et construit une case à cocher par tag. Best-effort : si
// ça échoue (serveur indisponible entre-temps, etc.), seule la case "aléatoire" reste utilisable —
// pas bloquant pour la création de partie.
async function chargerTagsDisponibles() {
  try {
    const response = await fetch(`${serverBaseUrl}/api/tags`);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    tagsDisponibles = await response.json();
  } catch (err) {
    console.error("Impossible de charger les thèmes disponibles :", err);
    tagsDisponibles = [];
  }
  renderTagPicker();
}

function renderTagPicker() {
  const container = el("setup-tags-container");
  // La case "aléatoire" est statique dans le HTML — on ne retire que les tuiles générées.
  container.querySelectorAll(".tag-tile:not(.tag-tile--random)").forEach((tile) => tile.remove());

  for (const tag of tagsDisponibles) {
    const label = document.createElement("label");
    label.className = "tag-tile";
    label.innerHTML = `<input type="checkbox" value="${escapeHtml(tag)}" /> ${escapeHtml(tag)}`;
    container.appendChild(label);
  }
}

// Cocher "aléatoire" grise et décoche tous les autres thèmes (le payload envoie alors tags: [],
// qui pioche dans tout le catalogue côté backend — voir RoundService.FiltrerParTagsOuGenres).
el("setup-tags-aleatoire").addEventListener("change", (e) => {
  const aleatoire = e.target.checked;
  el("setup-tags-container")
    .querySelectorAll(".tag-tile:not(.tag-tile--random) input")
    .forEach((input) => {
      input.disabled = aleatoire;
      if (aleatoire) input.checked = false;
    });
});

const ROUND_MODES = ["Qcm", "TapeReponse", "PremiereLettre"];

// Tiré aléatoirement par round plutôt que configuré manuellement — le contrat
// backend (roundModes) accepte un mode par round, la génération est laissée au code.
function pickRandomRoundModes(count) {
  return Array.from({ length: count }, () => ROUND_MODES[Math.floor(Math.random() * ROUND_MODES.length)]);
}

// Case "aléatoire" cochée -> tags: [] (tout le catalogue). Sinon, un thème coché ou plus.
function themesSelectionnes() {
  if (el("setup-tags-aleatoire").checked) return [];
  return Array.from(el("setup-tags-container").querySelectorAll(".tag-tile:not(.tag-tile--random) input:checked")).map(
    (input) => input.value
  );
}

// ----- Thème par série -----
//
// Retour utilisateur : une série = un thème, jamais un mélange (contrairement à la sélection de
// thèmes elle-même, qui reste multi-choix — les cases cochées forment le VIVIER de thèmes candidats,
// pas un filtre combiné). "J'imagine 5 séries et seules 3 vont être jouées, il faut que ce soit
// aléatoire" : chaque série tirée reçoit un thème distinct pioché au hasard dans ce vivier, sans
// répétition tant qu'il reste des thèmes non utilisés — au-delà (plus de séries que de thèmes
// cochés), le vivier est remélangé et repioché en boucle plutôt que de bloquer la configuration.

function melangerCopie(liste) {
  const copie = [...liste];
  for (let i = copie.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [copie[i], copie[j]] = [copie[j], copie[i]];
  }
  return copie;
}

// Retourne un tableau de longueur nombreSeries, chaque entrée étant la liste de tags (0 ou 1
// élément) à utiliser pour cette série. themes: [] (aléatoire) -> toutes les séries piochent dans
// tout le catalogue, comportement historique inchangé.
function assignerThemesAuxSeries(themes, nombreSeries) {
  if (themes.length === 0) return Array.from({ length: nombreSeries }, () => []);
  const assignation = [];
  let pioche = [];
  for (let i = 0; i < nombreSeries; i++) {
    if (pioche.length === 0) pioche = melangerCopie(themes);
    assignation.push([pioche.shift()]);
  }
  return assignation;
}

const LETTRES_SERIES = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
// Lettrage des séries (retour utilisateur : "la première série sera la série A") — au-delà de Z
// (26 séries, très au-delà de tout usage réel), retombe sur un numéro plutôt que de planter.
function lettreSerie(index) {
  return LETTRES_SERIES[index] ?? String(index + 1);
}

function capitaliser(texte) {
  return texte.length > 0 ? texte.charAt(0).toUpperCase() + texte.slice(1) : texte;
}

function libelleTheme(tags) {
  if (!tags || tags.length === 0) return "Aléatoire";
  return tags.map((t) => capitaliser(t.replace(/-/g, " "))).join(" + ");
}

// Thèmes réellement assignés à chaque série de la partie courante (index -> liste de tags),
// mémorisé côté client à la création (voir btn-create-game) pour affichage — le serveur connaît
// Series.Tags mais ne le renvoie pas nommé, seul l'index de série est envoyé (voir RoundStarted).
let tagsParSerieCourante = [];

// Libellé "(Série B — Rock)" pour les écrans round/bonus — vide si une seule série (pas de
// délimitation à afficher). Partagé par tous les libellés round/bonus pour rester cohérent.
function libelleSerie(index) {
  if (nombreSeriesTotal <= 1) return "";
  return ` (Série ${lettreSerie(index)} — ${libelleTheme(tagsParSerieCourante[index])})`;
}

// Palier de base (série 1), plafond visé pour le palier le plus haut de la DERNIÈRE série (voir
// architecture.md / blindify-rules : "jusqu'à 3000 pts") — retour utilisateur explicite : "imagine
// 10 séries, à la fin la plus grosse mise est 3000 points". La raison géométrique est donc calculée
// à partir du nombre de séries réellement choisi pour CETTE partie (pas une constante fixe) : avec
// nombreSeries séries, le palier 50 de la dernière série (index nombreSeries-1) vaut exactement
// PLAFOND_PALIER_MAX. Une seule série -> pas de progression possible, on garde la base telle quelle.
const PALIERS_BASE = [10, 20, 30, 50];
const PLAFOND_PALIER_MAX = 3000;
function paliersPourSerie(indexSerie, nombreSeriesTotal) {
  if (nombreSeriesTotal <= 1) return [...PALIERS_BASE];
  const dernierPalierBase = PALIERS_BASE[PALIERS_BASE.length - 1];
  const raison = Math.pow(PLAFOND_PALIER_MAX / dernierPalierBase, 1 / (nombreSeriesTotal - 1));
  const facteur = Math.pow(raison, indexSerie);
  return PALIERS_BASE.map((v) => Math.round(v * facteur));
}

// Volontairement minimal (retour utilisateur du 2026-08-24) : crée seulement le lobby, la
// configuration du blindtest est soumise séparément par buildConfigurerPartieRequest.
function buildCreateGameRequest() {
  const modeEquipe = el("setup-mode-equipe").checked;
  const nomsEquipes = modeEquipe
    ? el("setup-noms-equipes")
        .value.split(",")
        .map((s) => s.trim())
        .filter(Boolean)
    : [];

  return { modeEquipe, nomsEquipes };
}

function buildConfigurerPartieRequest() {
  const nombreSeries = Math.max(1, parseInt(el("setup-nombre-series").value, 10) || 1);
  const tagsParSerie = assignerThemesAuxSeries(themesSelectionnes(), nombreSeries);

  const nombreRounds = parseInt(el("setup-nombre-rounds").value, 10);
  const dureeFenetreMs = parseInt(el("setup-duree-fenetre").value, 10) * 1000;

  // Une SeriesSetupDto par série demandée — mêmes réglages de round pour toutes (durée, points,
  // pénalités...), seuls les paliers de mise bonus (voir paliersPourSerie) et le thème (voir
  // assignerThemesAuxSeries) varient avec l'index. Valeurs par défaut pour les paramètres non
  // exposés dans cet écran de configuration (à affiner plus tard) — restent configurables par
  // partie via ce payload, jamais en dur côté serveur.
  const seriesSetups = Array.from({ length: nombreSeries }, (_, index) => ({
    config: {
      nombreRoundsClassiques: nombreRounds,
      dureeFenetreReponseMs: dureeFenetreMs,
      pointsMax: 100,
      pointsMin: 20,
      penaliteMauvaiseReponseRatio: 0.5,
      penaliteAbsenceReponse: -5,
      paliersDeMise: paliersPourSerie(index, nombreSeries),
      dureePhaseMiseMs: 15000,
      dureePhaseQuestionMs: 20000,
    },
    roundModes: pickRandomRoundModes(nombreRounds),
    tags: tagsParSerie[index],
  }));

  return { seriesSetups, config: null };
}

el("setup-mode-equipe").addEventListener("change", (e) => {
  el("setup-equipes-wrapper").classList.toggle("hidden", !e.target.checked);
});

el("btn-create-game").addEventListener("click", async () => {
  const errorEl = el("setup-error");
  errorEl.textContent = "";

  try {
    const payload = buildCreateGameRequest();
    const result = await connection.invoke("CreateGame", payload);
    gameCode = result.code;
    hostSecret = result.hostSecret;
    partieConfiguree = false;
    el("btn-start-round").disabled = true;
    el("configurer-error").textContent = "";
    el("configurer-note").textContent = "";
    roundsDemarres = 0;
    scoreHistory = [];
    dernierScoreDto = null;
    historiqueLabelEnAttente = null;
    players = [];
    equipes = result.teams ?? [];
    equipeParJoueur = {};
    el("lobby-code").textContent = gameCode;
    renderPlayers();
    showScreen("screen-lobby");

    currentDisplayScreen = "lobby";
    currentRoundInfo = {};
    currentRevealInfo = {};
    currentScoresInfo = null;
    syncDisplay();
  } catch (err) {
    console.error(err);
    errorEl.textContent = "Erreur : " + (err.message || err);
  }
});

// Rappelable autant de fois que nécessaire tant que la partie n'a pas démarré (voir
// GameHub.ConfigurerPartie) — un essai raté (ex. thème trop niche pour le nombre de rounds
// demandé) laisse la configuration précédente intacte côté serveur, donc "Démarrer la partie"
// reste utilisable si une configuration avait déjà réussi auparavant pour ce lobby.
el("btn-configurer-partie").addEventListener("click", async () => {
  const errorEl = el("configurer-error");
  errorEl.textContent = "";
  el("configurer-note").textContent = "";

  if (!el("setup-tags-aleatoire").checked && themesSelectionnes().length === 0) {
    errorEl.textContent = "Coche au moins un thème, ou « Thème aléatoire ».";
    return;
  }

  try {
    const payload = buildConfigurerPartieRequest();
    await connection.invoke("ConfigurerPartie", payload);
    // Même valeur pour chaque série (champ partagé du formulaire) — voir buildConfigurerPartieRequest.
    nombreRoundsParSerie = payload.seriesSetups[0].config.nombreRoundsClassiques;
    nombreSeriesTotal = payload.seriesSetups.length;
    tagsParSerieCourante = payload.seriesSetups.map((s) => s.tags);
    serieCouranteIndex = 0;
    partieConfiguree = true;
    el("btn-start-round").disabled = false;
    el("configurer-note").textContent = "Configuration validée — prêt à démarrer.";
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

  // Vérifié côté client avant d'appeler le serveur : la série classique EN COURS est épuisée, on
  // enchaîne directement sur SA question bonus plutôt que d'attendre une intervention. Attention à
  // ne jamais appeler NextRound() ici dans ce cas précis : passé le dernier round d'une série,
  // NextRound() avancerait tout seul à la série suivante côté backend (voir GameHub.NextRound) et
  // sauterait silencieusement la question bonus de la série qu'on vient de terminer.
  if (roundsDemarres >= nombreRoundsParSerie) {
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

  // Même délai que l'enchaînement classique avant de basculer sur la question bonus — sans ça
  // le reveal du dernier round de la série s'affiche pendant 0s, écrasé instantanément par l'écran
  // de mise bonus (retour utilisateur : le round suivant ce dernier reveal disparaissait avant
  // d'avoir pu être lu).
  const delaiMs = Math.max(1, parseInt(el("setup-delai-enchainement").value, 10) || 10) * 1000;
  const finA = Date.now() + delaiMs;
  const label = roundsDemarres >= nombreRoundsParSerie ? "Question bonus" : "Round suivant";

  const updateNote = () => {
    const restant = Math.max(0, Math.ceil((finA - Date.now()) / 1000));
    el("auto-next-note").textContent = `${label} dans ${restant}s...`;
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

  const delaiMs = Math.max(1, parseInt(el("setup-delai-enchainement").value, 10) || 10) * 1000;
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

// Bascule effectivement sur la série suivante (NextRound() franchit la frontière de série côté
// serveur puisque RoundCourantIndex est déjà au dernier index — voir GameHub.NextRound) puis
// affiche l'écran d'annonce de la nouvelle série (voir afficherIntroSerie) — c'est cet écran qui
// démarrera le round, pas cette fonction. Partagé par le délai automatique (scheduleAutoNextSerie)
// et le bouton "Série suivante maintenant" (voir btn-end-now, series-aware).
async function avancerSerieSuivante() {
  roundsDemarres = 0; // nouvelle série : le compteur "rounds démarrés dans la série courante" repart de 0
  try {
    await connection.invoke("NextRound");
    afficherIntroSerie(serieCouranteIndex);
  } catch (err) {
    console.error(err);
    el("bonus-end-note").textContent = "Erreur au démarrage de la série suivante : " + (err.message || err);
  }
}

// ----- Écran d'annonce de série (avant le premier round de CHAQUE série, y compris la première) -----
// Retour utilisateur : "il faudrait avoir un écran avant chaque début de série indiquant la série."

let autoIntroTimeoutId = null;
let autoIntroIntervalId = null;
const DUREE_INTRO_SERIE_MS = 5000;

function cancelAutoIntroSerie() {
  if (autoIntroTimeoutId !== null) {
    clearTimeout(autoIntroTimeoutId);
    autoIntroTimeoutId = null;
  }
  if (autoIntroIntervalId !== null) {
    clearInterval(autoIntroIntervalId);
    autoIntroIntervalId = null;
  }
  el("serie-intro-note").textContent = "";
}

function demarrerRoundApresIntro() {
  cancelAutoIntroSerie();
  connection.invoke("StartRound").catch((err) => {
    console.error(err);
    el("serie-intro-error").textContent = "Erreur : " + (err.message || err);
  });
}

function scheduleAutoStartSerie() {
  cancelAutoIntroSerie();
  const finA = Date.now() + DUREE_INTRO_SERIE_MS;

  const updateNote = () => {
    const restant = Math.max(0, Math.ceil((finA - Date.now()) / 1000));
    el("serie-intro-note").textContent = `Démarrage automatique dans ${restant}s...`;
  };
  updateNote();
  autoIntroIntervalId = setInterval(updateNote, 250);

  autoIntroTimeoutId = setTimeout(demarrerRoundApresIntro, DUREE_INTRO_SERIE_MS);
}

// Affiché avant le premier round de la série `index` — panneau host, écran public (voir
// syncDisplay/display.js:renderSerieIntro) ET désormais l'app joueur (retour utilisateur du
// 2026-08-24 : AnnoncerSerieCourante diffuse la même annonce aux téléphones, voir GameHub.cs).
// S'enchaîne automatiquement sur StartRound après un court délai, ou immédiatement via le bouton
// "Démarrer la série".
function afficherIntroSerie(index) {
  connection.invoke("AnnoncerSerieCourante").catch((err) => console.error("AnnoncerSerieCourante:", err));

  el("serie-intro-lettre").textContent = `Série ${lettreSerie(index)}`;
  el("serie-intro-theme").textContent = libelleTheme(tagsParSerieCourante[index]);
  el("serie-intro-error").textContent = "";
  showScreen("screen-serie-intro");

  currentDisplayScreen = "serie-intro";
  currentSerieIntroInfo = { lettre: lettreSerie(index), theme: libelleTheme(tagsParSerieCourante[index]) };
  syncDisplay();

  scheduleAutoStartSerie();
}

el("btn-start-serie").addEventListener("click", demarrerRoundApresIntro);

// Enchaîne sur la série suivante après le résultat de la question bonus (au lieu de terminer la
// partie) quand il en reste — réutilise le même écran/minuteur que scheduleAutoEndGame (mutuellement
// exclusifs sur screen-bonus-result, jamais actifs en même temps).
function scheduleAutoNextSerie() {
  cancelAutoEnd();

  const delaiMs = Math.max(1, parseInt(el("setup-delai-enchainement").value, 10) || 10) * 1000;
  const finA = Date.now() + delaiMs;

  const updateNote = () => {
    const restant = Math.max(0, Math.ceil((finA - Date.now()) / 1000));
    el("bonus-end-note").textContent = `Série suivante${libelleSerie(serieCouranteIndex)} dans ${restant}s...`;
  };
  updateNote();
  autoEndIntervalId = setInterval(updateNote, 250);

  autoEndTimeoutId = setTimeout(() => {
    cancelAutoEnd();
    avancerSerieSuivante();
  }, delaiMs);
}

// ----- Lobby / round -----

// Extrait du click listener pour être aussi déclenchable depuis l'écran public (retour utilisateur :
// pénible de switcher vers le panneau de contrôle juste pour lancer — voir le listener "message" plus
// haut, qui relaie un clic sur le bouton équivalent de display.html).
function lancerLaPartieDepuisLobby() {
  el("lobby-error").textContent = "";

  // Garde-fou client (le serveur refuserait StartRound de toute façon, voir GameHub.StartRound) —
  // évite une HubException confuse si ce déclenchement vient de l'écran public (bouton toujours
  // visible là-bas, contrairement à btn-start-round qui est nativement désactivé ici).
  if (!partieConfiguree) {
    el("lobby-error").textContent = "Configure le blindtest avant de démarrer la partie.";
    return;
  }

  afficherIntroSerie(serieCouranteIndex); // toujours 0 ici (première série de la partie)
}

el("btn-start-round").addEventListener("click", lancerLaPartieDepuisLobby);

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
  leaderboardOpen = false;
  sendToDisplay({ type: "leaderboard-hide" });
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

// Series-aware : "Série suivante maintenant" (séries restantes) ou "Terminer maintenant" (dernière
// série) — le libellé est mis à jour dans le handler BonusResult, cohérent avec scheduleAutoNextSerie
// / scheduleAutoEndGame ci-dessus (ce bouton ne fait que sauter leur délai, jamais l'inverse).
el("btn-end-now").addEventListener("click", async () => {
  cancelAutoEnd();
  if (serieCouranteIndex < nombreSeriesTotal) {
    await avancerSerieSuivante();
    return;
  }
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

// ----- Démarrage : connexion automatique à l'adresse depuis laquelle la page a été chargée -----
// Retour utilisateur (2026-08-27) : cet écran est "useless" en usage normal — host/ est servi par
// le backend lui-même (voir docs/architecture.md "Dockerisation"), donc l'adresse du serveur est
// toujours celle de la barre d'adresse du navigateur, pas la peine de la faire retaper. Repli sur
// la dernière adresse connue (localStorage) si l'origine n'est pas exploitable (page ouverte en
// fichier local, ex. `file://`) — puis sur la saisie manuelle en cas d'échec/timeout, même logique
// de tentative silencieuse et bornée que côté Flutter (GameConnection.init).
const DELAI_RECONNEXION_AUTO_MS = 4000;
const origineActuelle = window.location.origin.startsWith("http") ? window.location.origin : null;
const derniereAdresseConnue = localStorage.getItem(SERVER_URL_STORAGE_KEY);
const adresseAutoConnexion = origineActuelle || derniereAdresseConnue;

showScreen("screen-connect");

if (adresseAutoConnexion) {
  el("server-url").value = adresseAutoConnexion;
  const btnConnect = el("btn-connect");
  btnConnect.disabled = true;
  btnConnect.textContent = "Connexion en cours...";
  tenterConnexion(adresseAutoConnexion, { silencieux: true, timeoutMs: DELAI_RECONNEXION_AUTO_MS }).finally(() => {
    btnConnect.disabled = false;
    btnConnect.textContent = "Se connecter";
  });
}
