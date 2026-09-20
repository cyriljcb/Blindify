"use strict";

// Écran public — fenêtre séparée du panneau de contrôle (host/index.html + main.js), voir
// CLAUDE.md et la note affichée dans le panneau de contrôle. Ne se connecte jamais à SignalR ni
// au serveur directement : reçoit uniquement des messages postMessage() du panneau de contrôle
// (qui, lui, détient la vraie connexion SignalR et joue l'audio). Volontairement minimaliste :
// pas de scores en continu, pas d'info sur le morceau avant le reveal — voir docs/architecture.md.
//
// Suit le même schéma état→rendu que le panneau de contrôle (docs/refactor-decisions.md section 2) :
// appliquerEtat() ne fait que muter `state`, render(state) qui suit repeint tout le contenu — mais
// sans découpage en fichiers séparés (state.js/handlers.js/render.js), pas justifié pour un simple
// récepteur de ~250 lignes.

import { escapeHtml, libelleReveal } from "./shared/format.js";
import { avatarHtml, renderScoreList, renderScoreChart, renderJoinQrCode, renderTitrePanel } from "./shared/components.js";

const el = (id) => document.getElementById(id);

const state = {
  screen: "idle",
  gameCode: null,
  serverBaseUrl: "",
  paused: false,
  players: [],
  equipeParJoueur: {},
  equipes: [],
  timerEndAt: null,
  timerDurationMs: 0,
  round: {},
  reveal: {},
  bonus: {},
  scores: null,
  titres: [],
  titreIndexAffiche: -1,
  serieIntro: {},
  scoreHistory: [],
  // {playerId, tempsEcouleMs}[], dans l'ordre d'arrivée — jamais l'exactitude de la réponse (voir
  // note en tête de fichier). Vidé à chaque nouveau round/question bonus, voir
  // "player-answered-reset" ci-dessous.
  playersAnswered: [],
};

function roster() {
  return { joueurs: state.players, equipes: state.equipes };
}

// Un seul champ affiché par option (titre, premier auteur, ou film) — même règle que côté
// Flutter (round_screen.dart:_QcmAnswers) : un morceau à plusieurs auteurs listés en entier
// rendrait le QCM illisible sur grand écran.
function libelleOptionQcm(option, cible) {
  if (cible === "Auteur") return option.artist.split(",")[0].trim();
  if (cible === "Film") return option.film;
  return option.title;
}

// Options QCM sur l'écran public (retour utilisateur) — masqué pour les autres modes (première
// lettre, tape la réponse) où round.qcmOptions est absent. Sans risque de spoil : ce sont les
// mêmes choix déjà visibles sur le téléphone de chaque joueur, jamais la bonne réponse seule.
function renderQcmOptionsDisplay(container, round) {
  const options = round?.qcmOptions;
  if (!options || options.length === 0) {
    container.classList.add("hidden");
    container.innerHTML = "";
    return;
  }
  container.classList.remove("hidden");
  container.innerHTML = options
    .map((o) => `<li>${escapeHtml(libelleOptionQcm(o, round.cible))}</li>`)
    .join("");
}

function afficherCover(imgEl, placeholderEl, coverPath) {
  if (coverPath && state.serverBaseUrl) {
    imgEl.src = `${state.serverBaseUrl}/files/${coverPath}`;
    imgEl.classList.remove("hidden");
    placeholderEl.classList.add("hidden");
  } else {
    imgEl.classList.add("hidden");
    imgEl.removeAttribute("src");
    placeholderEl.classList.remove("hidden");
  }
}

function renderPlayerList(container, joueurs) {
  container.innerHTML = "";
  for (const p of joueurs) {
    const li = document.createElement("li");
    if (!p.estConnecte) li.classList.add("disconnected");
    li.innerHTML = `<div class="player-row">${avatarHtml(p.playerId, p.nom, undefined, roster())}<span>${escapeHtml(p.nom)}</span></div>`;
    container.appendChild(li);
  }
}

function renderPlayerChips(container, joueurs) {
  container.innerHTML = "";
  for (const p of joueurs) {
    const li = document.createElement("li");
    li.className = "player-chip" + (p.estConnecte ? "" : " disconnected");
    li.innerHTML = `${avatarHtml(p.playerId, p.nom, undefined, roster())}<span>${escapeHtml(p.nom)}</span>`;
    container.appendChild(li);
  }
}

// Retour utilisateur : "faux" et "pas répondu" rendaient tous les deux en rouge, impossible à
// distinguer sur l'écran public. Round classique (RoundResultEntryDto) : estCorrecte est déjà
// nullable côté backend — null signifie explicitement "aucune réponse enregistrée" (voir
// RoundTimerCoordinator.cs, reponse?.EstCorrecte). Question bonus (BonusResultEntryDto) :
// estCorrecte n'est PAS nullable — un timeout y est délibérément traité comme une perte
// (BonusRoundService.TerminerParTimeout, "les hésitants ne doivent pas être avantagés" — voir
// architecture.md section 6) et synthétise une réponse vide ("") pour ce joueur. Une réponse vide
// n'arrive jamais pour un vrai essai (les modes QCM/lettre soumettent toujours une valeur, et
// l'auto-submit texte ignore un champ vide) — donc estCorrecte === false && reponse vide identifie
// fiablement ce cas côté écran public, sans changer le contrat serveur.
function statutReponse(r) {
  if (r.estCorrecte === null || r.estCorrecte === undefined) return "sans-reponse";
  if (r.estCorrecte === false && !r.reponse) return "sans-reponse";
  return r.estCorrecte ? "correct" : "incorrect";
}

const ICONES_STATUT = { correct: "✓", incorrect: "✗", "sans-reponse": "–" };

// Résultats sans le détail des points — seulement correct/incorrect/sans réponse (voir note en
// tête de fichier).
function renderResultBadges(container, resultats, joueurs) {
  container.innerHTML = "";
  for (const r of resultats) {
    const joueur = joueurs.find((j) => j.playerId === r.playerId);
    const nom = joueur ? joueur.nom : r.playerId;
    const statut = statutReponse(r);
    const li = document.createElement("li");
    li.className = `result-badge result-badge--${statut}`;
    li.innerHTML = `${avatarHtml(r.playerId, nom, undefined, roster())}<span>${escapeHtml(nom)}</span><span class="result-icon">${ICONES_STATUT[statut]}</span>`;
    container.appendChild(li);
  }
}

// Panneau flottant (voir answer-speed-panel dans display.html) — rang d'arrivée + temps, jamais
// si la réponse était correcte (uniquement connu du backend via ScoreUpdate, jamais transmis ici).
function renderAnswerSpeedPanel() {
  const panel = el("answer-speed-panel");
  const list = el("answer-speed-list");
  if (state.playersAnswered.length === 0) {
    panel.classList.add("hidden");
    list.innerHTML = "";
    return;
  }

  panel.classList.remove("hidden");
  list.innerHTML = state.playersAnswered
    .map(({ playerId, tempsEcouleMs }, index) => {
      const joueur = state.players.find((p) => p.playerId === playerId);
      const nom = joueur ? joueur.nom : "?";
      const secondes = (tempsEcouleMs / 1000).toFixed(1);
      return `<li><span class="answer-speed-rank">${index + 1}</span><span class="answer-speed-name">${escapeHtml(nom)}</span><span class="answer-speed-time">${secondes}s</span></li>`;
    })
    .join("");
}

let shakeTimeout = null;
let flashTimeout = null;

// Marge sous laquelle une réponse est considérée "auto-soumise à l'expiration du minuteur" plutôt
// que réellement tapée — un peu plus large que _margeAutoSubmitMs côté Flutter (1000ms) pour
// couvrir la latence réseau avant que le serveur n'horodate la réponse.
const MARGE_FIN_ROUND_MS = 1500;

// Secousse de tout l'écran + flash plein écran (voir @keyframes screen-shake/flash-overlay-pulse,
// display.css) — retour utilisateur : rendre visible depuis le fond de la salle qu'un joueur vient
// de répondre, sans rien révéler. Le flash double la secousse : une télé qui lisse le mouvement
// (traitement d'image) peut atténuer un simple déplacement de quelques pixels, un flash de couleur
// reste perceptible dans tous les cas.
function triggerScreenShake() {
  const main = document.querySelector("main");
  const flash = el("flash-overlay");

  main.classList.remove("screen-shake");
  flash.classList.remove("flash-overlay--active");
  // Force un reflow pour pouvoir rejouer l'animation même si un joueur répond deux fois de suite
  // très vite (retirer puis ré-ajouter la classe sans reflow entre les deux ne relance rien).
  void main.offsetWidth;
  void flash.offsetWidth;
  main.classList.add("screen-shake");
  flash.classList.add("flash-overlay--active");

  clearTimeout(shakeTimeout);
  shakeTimeout = setTimeout(() => main.classList.remove("screen-shake"), 420);
  clearTimeout(flashTimeout);
  flashTimeout = setTimeout(() => flash.classList.remove("flash-overlay--active"), 420);
}

const screens = [
  "screen-idle",
  "screen-lobby",
  "screen-serie-intro",
  "screen-round",
  "screen-round-ended",
  "screen-bonus-stake",
  "screen-bonus-question",
  "screen-bonus-result",
  "screen-ended",
];

// Même fondu que le panneau de contrôle (render.js:showScreen) — retour utilisateur : les
// transitions étaient encore trop brusques sur l'écran public.
const DUREE_TRANSITION_MS = 350;

function showScreen(id) {
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

// ----- Minuteur : rendu local à partir du timestamp absolu transmis par le panneau de contrôle,
// donc toujours synchronisé même avec un léger délai de message. -----

let timerInterval = null;

// secondesEl : retour utilisateur — la barre seule ne suffisait pas, il faut un décompte chiffré
// visible depuis le fond de la salle sur l'écran public.
function startLocalTimer(fillEl, secondesEl) {
  clearInterval(timerInterval);
  const update = () => {
    if (state.timerEndAt === null) return;
    const remaining = Math.max(0, state.timerEndAt - Date.now());
    const pct = state.timerDurationMs > 0 ? (remaining / state.timerDurationMs) * 100 : 0;
    fillEl.style.width = `${pct}%`;
    fillEl.classList.toggle("timer-fill--warn", pct <= 40 && pct > 15);
    fillEl.classList.toggle("timer-fill--danger", pct <= 15);
    if (secondesEl) {
      secondesEl.textContent = `${Math.ceil(remaining / 1000)}s`;
      secondesEl.classList.toggle("timer-seconds--warn", pct <= 40 && pct > 15);
      secondesEl.classList.toggle("timer-seconds--danger", pct <= 15);
    }
    if (remaining <= 0) clearInterval(timerInterval);
  };
  update();
  timerInterval = setInterval(update, 100);
}

function stopLocalTimer(fillEl, secondesEl) {
  clearInterval(timerInterval);
  if (fillEl) fillEl.style.width = "0%";
  if (secondesEl) {
    secondesEl.textContent = "";
    secondesEl.classList.remove("timer-seconds--warn", "timer-seconds--danger");
  }
}

// ----- Rendu -----

function render() {
  el("paused-banner").classList.toggle("hidden", !state.paused);

  switch (state.screen) {
    case "lobby":
      el("lobby-code").textContent = state.gameCode ?? "";
      renderJoinQrCode(el("lobby-qrcode"), state.serverBaseUrl, state.gameCode);
      renderPlayerList(el("lobby-players"), state.players);
      showScreen("screen-lobby");
      stopLocalTimer(null);
      break;

    case "serie-intro":
      el("serie-intro-lettre").textContent = state.serieIntro?.lettre ? `Série ${state.serieIntro.lettre}` : "";
      el("serie-intro-theme").textContent = state.serieIntro?.theme ?? "";
      showScreen("screen-serie-intro");
      stopLocalTimer(null);
      break;

    case "round": {
      const cibleLabel = state.round?.cible === "Titre" ? "le titre" : state.round?.cible === "Auteur" ? "l'artiste" : "le film";
      el("round-mode-label").textContent = `${state.round?.mode ?? ""} — trouver ${cibleLabel}${state.round?.serieLabel ?? ""}`;
      renderQcmOptionsDisplay(el("round-qcm-options"), state.round);
      renderPlayerChips(el("round-players"), state.players);
      showScreen("screen-round");
      if (!state.paused && state.timerEndAt !== null) startLocalTimer(el("timer-fill"), el("timer-seconds"));
      else stopLocalTimer(el("timer-fill"), el("timer-seconds"));
      break;
    }

    case "round-ended": {
      const revealCible = libelleReveal(state.reveal);
      el("reveal-title").textContent = revealCible.titre;
      el("reveal-artist").textContent = revealCible.sousTitre;
      afficherCover(el("reveal-cover"), el("reveal-cover-placeholder"), state.reveal?.coverPath);
      renderResultBadges(el("reveal-results"), state.reveal?.resultats ?? [], state.players);
      showScreen("screen-round-ended");
      stopLocalTimer(null);
      break;
    }

    case "bonus-stake": {
      el("bonus-stake-title").textContent = `Question bonus — mise à l'aveugle${state.bonus?.serieLabel ?? ""}`;
      const list = el("bonus-paliers");
      list.innerHTML = "";
      (state.bonus?.paliers ?? []).forEach((valeur, index) => {
        const li = document.createElement("li");
        li.innerHTML = `<span>Palier ${index + 1}${index === 0 ? " (safe)" : ""}</span><span>${valeur} pts</span>`;
        list.appendChild(li);
      });
      showScreen("screen-bonus-stake");
      if (!state.paused && state.timerEndAt !== null) startLocalTimer(el("bonus-stake-timer-fill"), el("bonus-stake-timer-seconds"));
      else stopLocalTimer(el("bonus-stake-timer-fill"), el("bonus-stake-timer-seconds"));
      break;
    }

    case "bonus-question":
      el("bonus-question-title").textContent = `Question bonus — à deviner !${state.bonus?.serieLabel ?? ""}`;
      el("bonus-ralenti-note").textContent = state.bonus?.ralenti
        ? "Morceau ralenti, un seul essai, pas de dégressivité."
        : "Un seul essai, pas de dégressivité.";
      el("bonus-course-banner").classList.toggle("hidden", !state.bonus?.estCourse);
      el("screen-bonus-question").classList.toggle("screen--course", !!state.bonus?.estCourse);
      renderQcmOptionsDisplay(el("bonus-question-qcm-options"), state.bonus);
      showScreen("screen-bonus-question");
      if (!state.paused && state.timerEndAt !== null) startLocalTimer(el("bonus-question-timer-fill"), el("bonus-question-timer-seconds"));
      else stopLocalTimer(el("bonus-question-timer-fill"), el("bonus-question-timer-seconds"));
      break;

    case "bonus-result": {
      el("bonus-result-title").textContent = `Résultat de la question bonus${state.reveal?.serieLabel ?? ""}`;
      const bonusRevealCible = libelleReveal(state.reveal);
      el("bonus-reveal-title").textContent = bonusRevealCible.titre;
      el("bonus-reveal-artist").textContent = bonusRevealCible.sousTitre;
      afficherCover(el("bonus-reveal-cover"), el("bonus-reveal-cover-placeholder"), state.reveal?.coverPath);
      renderResultBadges(el("bonus-reveal-results"), state.reveal?.resultats ?? [], state.players);
      showScreen("screen-bonus-result");
      stopLocalTimer(null);
      break;
    }

    case "ended":
      if (state.scores) {
        renderScoreList(el("final-scores"), state.scores, roster());
        renderScoreChart(el("score-chart"), state.scoreHistory, state.scores, roster());
      }
      renderTitrePanel(
        {
          panel: el("titre-panel"),
          compteur: el("titre-compteur"),
          libelle: el("titre-libelle"),
          description: el("titre-description"),
          joueurs: el("titre-joueurs"),
        },
        state.titres,
        state.titreIndexAffiche,
        roster()
      );
      showScreen("screen-ended");
      stopLocalTimer(null);
      break;

    default:
      showScreen("screen-idle");
      stopLocalTimer(null);
  }
}

// ----- Application de l'état reçu -----

function appliquerEtat(msg) {
  state.screen = msg.screen;
  state.gameCode = msg.gameCode ?? null;
  state.serverBaseUrl = msg.serverBaseUrl ?? "";
  state.paused = !!msg.paused;
  if (msg.players) state.players = msg.players;
  if (msg.equipes) state.equipes = msg.equipes;
  state.equipeParJoueur = msg.equipeParJoueur ?? {};
  state.timerEndAt = msg.timerEndAt ?? null;
  state.timerDurationMs = msg.timerDurationMs ?? 0;
  state.round = msg.round ?? {};
  state.reveal = msg.reveal ?? {};
  state.bonus = msg.bonus ?? {};
  state.scores = msg.scores ?? null;
  state.titres = msg.titres ?? [];
  state.titreIndexAffiche = msg.titreIndexAffiche ?? -1;
  state.serieIntro = msg.serieIntro ?? {};
  state.scoreHistory = msg.scoreHistory ?? [];

  render();
}

window.addEventListener("message", (event) => {
  const msg = event.data;
  if (!msg || typeof msg !== "object") return;

  if (msg.type === "state") {
    appliquerEtat(msg);
  } else if (msg.type === "leaderboard-show") {
    renderScoreList(el("leaderboard-scores"), msg.scores, roster());
    el("leaderboard-overlay").classList.remove("hidden");
  } else if (msg.type === "leaderboard-hide") {
    el("leaderboard-overlay").classList.add("hidden");
  } else if (msg.type === "player-answered") {
    // Ignore un doublon (playerId déjà présent) plutôt que de le pousser deux fois — un seul
    // essai par joueur et par round côté serveur, mais un message dupliqué/retardé ne doit pas
    // fausser le classement affiché.
    if (!state.playersAnswered.some((p) => p.playerId === msg.playerId)) {
      state.playersAnswered.push({ playerId: msg.playerId, tempsEcouleMs: msg.tempsEcouleMs });
      renderAnswerSpeedPanel();
    }
    // Retour utilisateur : une réponse auto-soumise à l'expiration du minuteur (mode "tape la
    // réponse", voir AnswerPhaseScreen._autoSubmitSiSaisie côté Flutter) ne mérite pas de secousse
    // — le round se termine de toute façon dans la même seconde, ça n'apporte plus rien. Le
    // classement de rapidité, lui, reste à jour dans tous les cas (juste ci-dessus).
    const tempsRestantMs = state.timerDurationMs - msg.tempsEcouleMs;
    if (tempsRestantMs > MARGE_FIN_ROUND_MS) triggerScreenShake();
  } else if (msg.type === "player-answered-reset") {
    state.playersAnswered = [];
    renderAnswerSpeedPanel();
  }
});

// Demande un rattrapage d'état au panneau de contrôle (utile si cet écran est ouvert/rechargé
// après que la partie a déjà commencé).
if (window.opener) {
  window.opener.postMessage({ type: "request-sync" }, "*");
}

// Retour utilisateur : lancer le blindtest directement depuis l'écran public plutôt que de devoir
// switcher vers le panneau de contrôle — relayé via postMessage, voir display-bridge.js:"start-round".
el("btn-launch-from-display")?.addEventListener("click", () => {
  if (window.opener) window.opener.postMessage({ type: "start-round" }, "*");
});

showScreen("screen-idle");
