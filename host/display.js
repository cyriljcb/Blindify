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

import { escapeHtml, libelleReveal, libelleTheme, questionCible, libelleMode } from "./shared/format.js";
import { avatarHtml, renderScoreList, renderScoreChart, renderJoinQrCode, renderTitrePanel, formeQcmSvg } from "./shared/components.js";
import { iconeSvg } from "./shared/icons.js";

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
  // Retour utilisateur (2026-10-17) — playerId ayant verrouillé leur mise bonus (jamais le palier),
  // décompte du vote des thèmes au lobby.
  misesEnregistrees: [],
  votesThemes: null,
};

// Joueurs encore attendus : un joueur parti volontairement (QuitterPartie) reste dans le roster pour
// les noms/couleurs du classement, mais n'est plus compté ni affiché comme « en train de répondre ».
function joueursPresents() {
  return state.players.filter((p) => !p.aQuitte);
}

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
// Refonte UI (lot 1) : cible Année en QCM incluse (anneeOptions, des années en texte plutôt que
// des morceaux) — les téléphones l'affichent déjà en tuiles colorées, la TV restait vide.
function renderQcmOptionsDisplay(container, round) {
  const libelles =
    round?.cible === "Annee" && round?.anneeOptions?.length
      ? round.anneeOptions
      : (round?.qcmOptions ?? []).map((o) => libelleOptionQcm(o, round.cible));
  if (libelles.length === 0 || round?.mode !== "Qcm") {
    container.classList.add("hidden");
    container.innerHTML = "";
    return;
  }
  container.classList.remove("hidden");
  container.innerHTML = libelles
    .map((libelle, i) => `<li class="qcm-opt qcm-opt--${i % 4}">${formeQcmSvg(i)}<span>${escapeHtml(libelle)}</span></li>`)
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
  // aRepondu est calculé par le panneau de contrôle (handlers.js) : il ne transmet jamais le texte de
  // la réponse à l'écran public. Le test historique sur r.reponse, jamais transmis, affichait donc
  // toute mauvaise réponse comme « sans réponse » (corrigé lors de la refonte UI, lot 1).
  if (r.estCorrecte === false && !(r.aRepondu ?? !!r.reponse)) return "sans-reponse";
  return r.estCorrecte ? "correct" : "incorrect";
}

const ICONES_STATUT = { correct: "✓", incorrect: "✗", "sans-reponse": "–" };

// Refonte UI (lot 1) — « X / Y ont répondu » + avatars sous la pochette, pendant la question :
// remplace le panneau flottant « Rapidité » et les chips de joueurs. Jamais l'exactitude de la
// réponse (inconnue ici, voir note en tête de fichier) ; le classement de rapidité est réservé à
// l'écran de résultat (renderResultBadges).
function renderAnsweredBlock(container) {
  if (!container) return;
  const repondus = new Set(state.playersAnswered.map((p) => p.playerId));
  const presents = joueursPresents();
  const total = presents.length;
  const avatars = presents
    .map((p) => {
      const classe = repondus.has(p.playerId) ? "answered-avatar answered-avatar--done" : "answered-avatar";
      const titre = escapeHtml(p.nom) + (p.estConnecte ? "" : " (déconnecté)");
      return `<span class="${classe}" title="${titre}">${avatarHtml(p.playerId, p.nom, undefined, roster())}</span>`;
    })
    .join("");
  container.innerHTML = `
    <span class="answered-label">Ont répondu</span>
    <span class="answered-count">${presents.filter((p) => repondus.has(p.playerId)).length}<small> / ${total}</small></span>
    <div class="answered-avatars">${avatars}</div>`;
}

function renderAnsweredBlocks() {
  renderAnsweredBlock(el("round-answered"));
  renderAnsweredBlock(el("bonus-question-answered"));
}

// Résultats sans le détail des points — seulement correct/incorrect/sans réponse (voir note en
// tête de fichier). Refonte UI (lot 1) : triés par ordre d'arrivée des réponses avec le temps de
// chacun (le classement de rapidité, retiré de l'écran de question), puis ceux qui n'ont pas
// répondu.
function renderResultBadges(container, resultats, joueurs) {
  container.innerHTML = "";
  const ordre = new Map(state.playersAnswered.map((p, i) => [p.playerId, i]));
  const tries = [...resultats].sort(
    (a, b) => (ordre.get(a.playerId) ?? Infinity) - (ordre.get(b.playerId) ?? Infinity)
  );
  for (const r of tries) {
    const joueur = joueurs.find((j) => j.playerId === r.playerId);
    const nom = joueur ? joueur.nom : r.playerId;
    const statut = statutReponse(r);
    const arrivee = state.playersAnswered.find((p) => p.playerId === r.playerId);
    const temps = arrivee && statut !== "sans-reponse"
      ? `<span class="result-time">${(arrivee.tempsEcouleMs / 1000).toFixed(1)} s</span>`
      : "";
    const li = document.createElement("li");
    li.className = `result-badge result-badge--${statut}`;
    li.innerHTML = `${avatarHtml(r.playerId, nom, undefined, roster())}<span>${escapeHtml(nom)}</span>${temps}<span class="result-icon">${ICONES_STATUT[statut]}</span>`;
    container.appendChild(li);
  }
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
// visible depuis le fond de la salle sur l'écran public. chronoEl (retour utilisateur 2026-10-17) :
// chrono circulaire de la mise bonus à la place de la barre — arc SVG + classes d'urgence.
const CIRCONFERENCE_CHRONO = 2 * Math.PI * 52;

function startLocalTimer(fillEl, secondesEl, chronoEl = null) {
  clearInterval(timerInterval);
  const update = () => {
    if (state.timerEndAt === null) return;
    const remaining = Math.max(0, state.timerEndAt - Date.now());
    const pct = state.timerDurationMs > 0 ? (remaining / state.timerDurationMs) * 100 : 0;
    if (fillEl) {
      fillEl.style.width = `${pct}%`;
      fillEl.classList.toggle("timer-fill--warn", pct <= 40 && pct > 15);
      fillEl.classList.toggle("timer-fill--danger", pct <= 15);
    }
    if (chronoEl) {
      chronoEl.querySelector(".bonus-chrono__arc").style.strokeDashoffset = `${CIRCONFERENCE_CHRONO * (1 - pct / 100)}`;
      chronoEl.classList.toggle("bonus-chrono--warn", pct <= 40 && pct > 15);
      chronoEl.classList.toggle("bonus-chrono--danger", pct <= 15 || (remaining <= 5000 && remaining > 0));
    }
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

// ----- Mise bonus (retour utilisateur 2026-10-17 : « vraiment fade ») -----

// Du plus prudent au plus risqué — même ordre que les paliers (croissants) côté serveur.
const PALIERS_DISPLAY = [
  { nom: "Safe", accroche: "on assure" },
  { nom: "Malin", accroche: "un peu de risque" },
  { nom: "Audacieux", accroche: "ça passe ou ça casse" },
  { nom: "Tapis", accroche: "tout ou rien" },
];

// Reconstruit les paliers seulement quand ils changent (nouvelle question bonus) : l'écran reçoit un
// état complet à chaque événement, rejouer l'entrée en escalier à chaque fois serait épuisant.
let clePaliersAffiches = null;

function renderPaliersBonus(paliers) {
  const cle = paliers.join("|") + "|" + (state.bonus?.serieLettre ?? "");
  if (cle === clePaliersAffiches) return;
  clePaliersAffiches = cle;

  const max = Math.max(1, ...paliers);
  el("bonus-paliers").innerHTML = paliers
    .map((valeur, i) => {
      const infos = PALIERS_DISPLAY[i] ?? { nom: `Palier ${i + 1}`, accroche: "" };
      // Hauteur en escalier : jamais sous 38 % pour garder le texte lisible sur la première marche.
      const hauteur = 38 + (valeur / max) * 62;
      return `<li class="palier palier--${i % 4}" style="--i: ${i}; --h: ${hauteur}%">
        <div class="palier__carte">
          ${i === paliers.length - 1 ? `<span class="palier__flamme">${iconeSvg("etincelle")}</span>` : ""}
          <span class="palier__valeur"><small>±</small><b data-valeur="${valeur}">0</b></span>
          <span class="palier__nom">${escapeHtml(infos.nom)}</span>
          <span class="palier__accroche">${escapeHtml(infos.accroche)}</span>
        </div>
      </li>`;
    })
    .join("");

  animerCompteurs(el("bonus-paliers").querySelectorAll("b[data-valeur]"));
}

// Les valeurs « montent » de 0 à leur montant, décalées comme les marches.
function animerCompteurs(elements) {
  const reduit = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  elements.forEach((b, i) => {
    const cible = Number(b.dataset.valeur);
    if (reduit) {
      b.textContent = String(cible);
      return;
    }
    const delai = 250 + i * 160;
    const duree = 700;
    const depart = performance.now() + delai;
    const pas = (t) => {
      const p = Math.min(1, Math.max(0, (t - depart) / duree));
      b.textContent = String(Math.round(cible * (1 - Math.pow(1 - p, 3))));
      if (p < 1) requestAnimationFrame(pas);
    };
    requestAnimationFrame(pas);
    // Filet de sécurité : rAF peut être ralenti (onglet en arrière-plan) — la valeur finale est garantie.
    setTimeout(() => { b.textContent = String(cible); }, delai + duree + 100);
  });
}

// Mise à jour par joueur (jamais un innerHTML global) : une mise qui vient d'être posée déclenche son
// animation « verrouillée » une seule fois, même si l'état complet est rediffusé juste après.
function renderMisesBonus() {
  const liste = el("bonus-mises-joueurs");
  const presents = joueursPresents();
  const posees = new Set(state.misesEnregistrees);

  const existants = new Map([...liste.children].map((li) => [li.dataset.playerId, li]));
  for (const [id, li] of existants) {
    if (!presents.some((p) => p.playerId === id)) li.remove();
  }
  for (const p of presents) {
    let li = existants.get(p.playerId);
    if (!li) {
      li = document.createElement("li");
      li.className = "mise-joueur";
      li.dataset.playerId = p.playerId;
      li.innerHTML = `<span class="mise-joueur__avatar">${avatarHtml(p.playerId, p.nom, undefined, roster())}<span class="mise-joueur__jeton" aria-hidden="true"></span></span><span class="mise-joueur__nom">${escapeHtml(p.nom)}</span>`;
      liste.appendChild(li);
    }
    const etaitPosee = li.classList.contains("mise-joueur--posee");
    const estPosee = posees.has(p.playerId);
    li.classList.toggle("mise-joueur--posee", estPosee);
    li.classList.toggle("mise-joueur--off", !p.estConnecte && !estPosee);
    if (estPosee && !etaitPosee && existants.has(p.playerId)) {
      li.classList.remove("mise-joueur--pop");
      void li.offsetWidth;
      li.classList.add("mise-joueur--pop");
    }
  }

  const nb = presents.filter((p) => posees.has(p.playerId)).length;
  el("bonus-mises-compteur").innerHTML = `<b>${nb}</b><small> / ${presents.length}</small> mise${nb > 1 ? "s" : ""} posée${nb > 1 ? "s" : ""}`;
  el("bonus-mises-toutes").classList.toggle("hidden", presents.length === 0 || nb < presents.length);
}

// Flash bref couleur moutarde à chaque mise — plus léger que la secousse d'une réponse.
let flashMiseTimeout = null;
function flashMise() {
  const flash = el("flash-overlay");
  flash.classList.remove("flash-overlay--mise");
  void flash.offsetWidth;
  flash.classList.add("flash-overlay--mise");
  clearTimeout(flashMiseTimeout);
  flashMiseTimeout = setTimeout(() => flash.classList.remove("flash-overlay--mise"), 380);
}

// ----- Vote des thèmes au lobby (retour utilisateur 2026-10-17) -----

function renderVotesLobby() {
  const vote = state.votesThemes;
  const bloc = el("lobby-votes");
  bloc.classList.toggle("hidden", !vote);
  if (!vote) return;

  const presents = joueursPresents().length;
  el("lobby-votes-compteur").textContent = vote.ouvert ? `· ${vote.votants.length} / ${presents} ont voté` : "· vote clos";
  const max = Math.max(1, ...vote.votes.map((v) => v.nombre));
  el("lobby-votes-liste").innerHTML = vote.votes
    .slice(0, 8)
    .map(
      (v, i) => `<li class="lobby-vote lobby-vote--${i % 4}">
        <span class="lobby-vote__theme">${escapeHtml(libelleTheme([v.theme]))}</span>
        <span class="lobby-vote__barre"><i style="width: ${(v.nombre / max) * 100}%"></i></span>
        <b>${v.nombre}</b>
      </li>`
    )
    .join("");
}

// Badge « Rejoindre » en coin d'écran, partout sauf là où le code est déjà affiché en grand.
function renderJoinBadge() {
  const visible = !!state.gameCode && !["idle", "lobby"].includes(state.screen);
  el("join-badge").classList.toggle("hidden", !visible);
  if (!visible) return;
  el("join-badge-code").textContent = state.gameCode;
  renderJoinQrCode(el("join-badge-qr"), state.serverBaseUrl, state.gameCode);
}

// ----- Rendu -----

function render() {
  el("paused-banner").classList.toggle("hidden", !state.paused);
  // Refonte UI (lot 2) : fige l'égaliseur de la pochette mystère pendant la pause.
  document.body.classList.toggle("en-pause", state.paused);
  renderJoinBadge();
  if (state.screen !== "bonus-stake") clePaliersAffiches = null;

  switch (state.screen) {
    case "lobby":
      el("lobby-code").textContent = state.gameCode ?? "";
      renderJoinQrCode(el("lobby-qrcode"), state.serverBaseUrl, state.gameCode);
      renderPlayerList(el("lobby-players"), joueursPresents());
      renderVotesLobby();
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
      // Refonte UI (lot 1) : la question en très gros, le contexte (série, round, mode) en petit.
      el("round-eyebrow").textContent = [state.round?.serieCourt, state.round?.roundLabel, libelleMode(state.round?.mode)]
        .filter(Boolean)
        .join(" · ");
      el("round-question").textContent = questionCible(state.round?.cible);
      renderQcmOptionsDisplay(el("round-qcm-options"), state.round);
      renderAnsweredBlocks();
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
      el("bonus-stake-eyebrow").textContent = [state.bonus?.serieLettre ? `Série ${state.bonus.serieLettre}` : null, state.bonus?.theme]
        .filter(Boolean)
        .join(" · ");
      renderPaliersBonus(state.bonus?.paliers ?? []);
      renderMisesBonus();
      showScreen("screen-bonus-stake");
      if (!state.paused && state.timerEndAt !== null) startLocalTimer(null, el("bonus-stake-timer-seconds"), el("bonus-chrono"));
      else stopLocalTimer(null, el("bonus-stake-timer-seconds"));
      break;
    }

    case "bonus-question":
      el("bonus-question-eyebrow").textContent = ["Question bonus", state.bonus?.serieCourt, libelleMode(state.bonus?.mode)]
        .filter(Boolean)
        .join(" · ");
      el("bonus-question-question").textContent = questionCible(state.bonus?.cible);
      renderAnsweredBlocks();
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
  state.misesEnregistrees = msg.misesEnregistrees ?? [];
  state.votesThemes = msg.votesThemes ?? null;

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
      renderAnsweredBlocks();
    }
    // Retour utilisateur : une réponse auto-soumise à l'expiration du minuteur (mode "tape la
    // réponse", voir AnswerPhaseScreen._autoSubmitSiSaisie côté Flutter) ne mérite pas de secousse
    // — le round se termine de toute façon dans la même seconde, ça n'apporte plus rien. Le
    // classement de rapidité, lui, reste à jour dans tous les cas (juste ci-dessus).
    const tempsRestantMs = state.timerDurationMs - msg.tempsEcouleMs;
    if (tempsRestantMs > MARGE_FIN_ROUND_MS) triggerScreenShake();
  } else if (msg.type === "player-answered-reset") {
    state.playersAnswered = [];
    renderAnsweredBlocks();
  } else if (msg.type === "mise-enregistree") {
    if (state.screen === "bonus-stake") flashMise();
  } else if (msg.type === "joker-utilise") {
    afficherBanniereJoker(msg.playerId);
  }
});

// V2, section 12.7 — annonce transitoire, jamais l'effet ni la cible révélée. Un nouvel appel
// pendant l'affichage relance le délai plutôt que d'empiler les bannières (un seul joker à la
// fois est réaliste, mais reste robuste si deux joueurs l'utilisent coup sur coup).
const DUREE_BANNIERE_JOKER_MS = 3000;
let jokerBannerTimeoutId = null;

function afficherBanniereJoker(playerId) {
  const nom = (roster().joueurs ?? []).find((j) => j.playerId === playerId)?.nom ?? "Un joueur";
  const banner = el("joker-banner");
  banner.innerHTML = `${iconeSvg("etincelle")} ${escapeHtml(nom)} sort son joker`;
  banner.classList.remove("hidden");
  if (jokerBannerTimeoutId) clearTimeout(jokerBannerTimeoutId);
  jokerBannerTimeoutId = setTimeout(() => {
    banner.classList.add("hidden");
    jokerBannerTimeoutId = null;
  }, DUREE_BANNIERE_JOKER_MS);
}

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
