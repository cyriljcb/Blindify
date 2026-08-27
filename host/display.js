"use strict";

// Écran public — fenêtre séparée du panneau de contrôle (host/index.html + app.js), voir
// CLAUDE.md et la note affichée dans le panneau de contrôle. Ne se connecte jamais à SignalR ni
// au serveur directement : reçoit uniquement des messages postMessage() du panneau de contrôle
// (qui, lui, détient la vraie connexion SignalR et joue l'audio). Volontairement minimaliste :
// pas de scores en continu, pas d'info sur le morceau avant le reveal — voir docs/architecture.md.

let timerInterval = null;
let timerEndAt = null;
let timerDurationMs = 0;

const el = (id) => document.getElementById(id);

function escapeHtml(str) {
  const div = document.createElement("div");
  div.textContent = str ?? "";
  return div.innerHTML;
}

// Palette identique à host/app.js:couleurAvatar et app/lib/widgets/player_avatar.dart — voir
// commentaire là-bas (retour utilisateur : hash%360 laissait deux joueurs avec des teintes de
// vert trop proches).
const PALETTE_AVATARS = [
  "#E63946", "#457B9D", "#F4A300", "#2A9D8F",
  "#9B5DE5", "#06D6A0", "#F15BB5", "#4CC9F0",
  "#FF6B35", "#8AC926", "#FFCA3A", "#6A4C93",
];

// Dernier roster connu, mis à jour à chaque état reçu (voir appliquerEtat) — cette fenêtre ne se
// connecte jamais elle-même au serveur, donc pas d'autre source pour situer un id dans l'ordre
// d'arrivée des joueurs/équipes. Noms distincts des paramètres homonymes de renderScoreChart.
let rosterJoueurs = [];
let rosterEquipes = [];

function couleurAvatar(id) {
  const indexJoueur = rosterJoueurs.findIndex((p) => p.playerId === id);
  if (indexJoueur >= 0) return PALETTE_AVATARS[indexJoueur % PALETTE_AVATARS.length];

  const indexEquipe = rosterEquipes.findIndex((e) => (e.id ?? e.teamId) === id);
  if (indexEquipe >= 0) return PALETTE_AVATARS[(rosterJoueurs.length + indexEquipe) % PALETTE_AVATARS.length];

  let hash = 0;
  for (let i = 0; i < id.length; i++) hash = (hash * 31 + id.charCodeAt(i)) >>> 0;
  return `hsl(${hash % 360}, 65%, 55%)`;
}

// Évite de régénérer le SVG à chaque message d'état reçu (lobby renvoyé à chaque join/leave) —
// seul le couple serveur+code détermine le contenu du QR.
let dernierQrKey = null;

function renderJoinQrCode(serverBaseUrl, gameCode) {
  const container = el("lobby-qrcode");
  if (!container || !gameCode || !serverBaseUrl) return;

  const key = serverBaseUrl + "|" + gameCode;
  if (key === dernierQrKey) return;
  dernierQrKey = key;

  const payload = JSON.stringify({ server: serverBaseUrl, code: gameCode });
  const qr = qrcode(0, "M");
  qr.addData(payload);
  qr.make();
  container.innerHTML = qr.createSvgTag({ cellSize: 6, margin: 4, scalable: true });
}

const MEDAILLES = ["🥇", "🥈", "🥉"];

function avatarHtml(id, nom, rang) {
  if (rang !== undefined && rang < 3) {
    return `<span class="avatar" style="background: transparent; font-size: 1.6rem;">${MEDAILLES[rang]}</span>`;
  }
  const initiale = escapeHtml((nom || "?").trim().slice(0, 1).toUpperCase() || "?");
  return `<span class="avatar" style="background: ${couleurAvatar(id)};">${initiale}</span>`;
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

// Même fondu que le panneau de contrôle (host/app.js:showScreen) — retour utilisateur : les
// transitions étaient encore trop brusques sur l'écran public, qui se contentait de basculer les
// classes "hidden" instantanément (le fondu CSS .screen--leaving/--entering existe déjà dans
// style.css mais n'était appliqué que côté panneau de contrôle).
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

// Cible Film (morceaux "disney", voir RoundService.DemarrerRound / BonusRoundService.CreerBonusRound) :
// la réponse attendue était le film, pas le titre réel de la chanson — le reveal doit donc mettre
// le film en avant. Le vrai titre/artiste reste affiché en dessous, à titre de bonus trivia. Utilisé
// pour le reveal de round classique ET la question bonus (même forme de payload : title/artist/cible/film).
function libelleReveal(reveal) {
  if (reveal?.cible === "Film") {
    return { titre: reveal?.film ?? "", sousTitre: `${reveal?.title ?? ""} — ${reveal?.artist ?? ""}` };
  }
  return { titre: reveal?.title ?? "", sousTitre: reveal?.artist ?? "" };
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

function afficherCover(imgEl, placeholderEl, serverBaseUrl, coverPath) {
  if (coverPath && serverBaseUrl) {
    imgEl.src = `${serverBaseUrl}/files/${coverPath}`;
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
    li.innerHTML = `<div class="player-row">${avatarHtml(p.playerId, p.nom)}<span>${escapeHtml(p.nom)}</span></div>`;
    container.appendChild(li);
  }
}

function renderPlayerChips(container, joueurs) {
  container.innerHTML = "";
  for (const p of joueurs) {
    const li = document.createElement("li");
    li.className = "player-chip" + (p.estConnecte ? "" : " disconnected");
    li.innerHTML = `${avatarHtml(p.playerId, p.nom)}<span>${escapeHtml(p.nom)}</span>`;
    container.appendChild(li);
  }
}

// Résultats sans le détail des points — seulement correct/incorrect (voir note ci-dessus).
function renderResultBadges(container, resultats, joueurs) {
  container.innerHTML = "";
  for (const r of resultats) {
    const joueur = joueurs.find((j) => j.playerId === r.playerId);
    const nom = joueur ? joueur.nom : r.playerId;
    const li = document.createElement("li");
    li.className = "result-badge " + (r.estCorrecte ? "result-badge--correct" : "result-badge--incorrect");
    li.innerHTML = `${avatarHtml(r.playerId, nom)}<span>${escapeHtml(nom)}</span><span class="result-icon">${r.estCorrecte ? "✓" : "✗"}</span>`;
    container.appendChild(li);
  }
}

// Scores complets — uniquement pour le tableau général et la fin de partie, moments de reveal
// volontaire (voir docs/architecture.md section 7).
function renderScoreList(container, dto) {
  container.innerHTML = "";

  const joueurs = [...dto.joueurs].sort((a, b) => b.score - a.score);
  joueurs.forEach((j, index) => {
    const li = document.createElement("li");
    li.innerHTML = `
      <div class="player-row">${avatarHtml(j.playerId, j.nom, index)}<span>${escapeHtml(j.nom)}</span></div>
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
        <div class="player-row">${avatarHtml(eq.teamId, eq.nom, index)}<span>${escapeHtml(eq.nom)}</span></div>
        <span>${eq.score}</span>
      `;
      container.appendChild(li);
    });
  }
}

// Graphique "qui menait quand" — miroir de host/app.js:renderScoreChart. L'historique
// (scoreHistory, un point par série résolue) est calculé côté panneau de contrôle et reçu tel
// quel via syncDisplay plutôt que recalculé ici, pour garantir un rendu strictement identique
// aux deux endroits (retour utilisateur : afficher le graphique aussi sur l'écran public).
function renderScoreChart(container, history, finalDto, joueursConnus, equipesConnues) {
  container.innerHTML = "";
  if (!history || history.length === 0) return;

  const useEquipes = finalDto.equipes && finalDto.equipes.length > 0;
  const depart = {
    label: "Départ",
    joueurs: (joueursConnus ?? []).map((p) => ({ playerId: p.playerId, score: 0 })),
    equipes: (equipesConnues ?? []).map((e) => ({ teamId: e.id, score: 0 })),
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

// ----- Minuteur : rendu local à partir du timestamp absolu transmis par le panneau de contrôle,
// donc toujours synchronisé même avec un léger délai de message. -----

// secondesEl : retour utilisateur — la barre seule ne suffisait pas, il faut un décompte chiffré
// visible depuis le fond de la salle sur l'écran public.
function startLocalTimer(fillEl, secondesEl) {
  clearInterval(timerInterval);
  const update = () => {
    if (timerEndAt === null) return;
    const remaining = Math.max(0, timerEndAt - Date.now());
    const pct = timerDurationMs > 0 ? (remaining / timerDurationMs) * 100 : 0;
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

// ----- Application de l'état reçu -----

function appliquerEtat(msg) {
  timerEndAt = msg.timerEndAt ?? null;
  timerDurationMs = msg.timerDurationMs ?? 0;

  if (msg.players) rosterJoueurs = msg.players;
  if (msg.equipes) rosterEquipes = msg.equipes;

  el("paused-banner").classList.toggle("hidden", !msg.paused);

  switch (msg.screen) {
    case "lobby":
      el("lobby-code").textContent = msg.gameCode ?? "";
      renderJoinQrCode(msg.serverBaseUrl, msg.gameCode);
      renderPlayerList(el("lobby-players"), msg.players ?? []);
      showScreen("screen-lobby");
      stopLocalTimer(null);
      break;

    case "serie-intro":
      el("serie-intro-lettre").textContent = msg.serieIntro?.lettre ? `Série ${msg.serieIntro.lettre}` : "";
      el("serie-intro-theme").textContent = msg.serieIntro?.theme ?? "";
      showScreen("screen-serie-intro");
      stopLocalTimer(null);
      break;

    case "round": {
      const cibleLabel = msg.round?.cible === "Titre" ? "le titre" : msg.round?.cible === "Auteur" ? "l'artiste" : "le film";
      el("round-mode-label").textContent = `${msg.round?.mode ?? ""} — trouver ${cibleLabel}${msg.round?.serieLabel ?? ""}`;
      renderQcmOptionsDisplay(el("round-qcm-options"), msg.round);
      renderPlayerChips(el("round-players"), msg.players ?? []);
      showScreen("screen-round");
      if (!msg.paused && timerEndAt !== null) startLocalTimer(el("timer-fill"), el("timer-seconds"));
      else stopLocalTimer(el("timer-fill"), el("timer-seconds"));
      break;
    }

    case "round-ended": {
      const revealCible = libelleReveal(msg.reveal);
      el("reveal-title").textContent = revealCible.titre;
      el("reveal-artist").textContent = revealCible.sousTitre;
      afficherCover(el("reveal-cover"), el("reveal-cover-placeholder"), msg.serverBaseUrl, msg.reveal?.coverPath);
      renderResultBadges(el("reveal-results"), msg.reveal?.resultats ?? [], msg.players ?? []);
      showScreen("screen-round-ended");
      stopLocalTimer(null);
      break;
    }

    case "bonus-stake": {
      el("bonus-stake-title").textContent = `Question bonus — mise à l'aveugle${msg.bonus?.serieLabel ?? ""}`;
      const list = el("bonus-paliers");
      list.innerHTML = "";
      (msg.bonus?.paliers ?? []).forEach((valeur, index) => {
        const li = document.createElement("li");
        li.innerHTML = `<span>Palier ${index + 1}${index === 0 ? " (safe)" : ""}</span><span>${valeur} pts</span>`;
        list.appendChild(li);
      });
      showScreen("screen-bonus-stake");
      if (!msg.paused && timerEndAt !== null) startLocalTimer(el("bonus-stake-timer-fill"), el("bonus-stake-timer-seconds"));
      else stopLocalTimer(el("bonus-stake-timer-fill"), el("bonus-stake-timer-seconds"));
      break;
    }

    case "bonus-question":
      el("bonus-question-title").textContent = `Question bonus — à deviner !${msg.bonus?.serieLabel ?? ""}`;
      el("bonus-ralenti-note").textContent = msg.bonus?.ralenti
        ? "Morceau ralenti, un seul essai, pas de dégressivité."
        : "Un seul essai, pas de dégressivité.";
      renderQcmOptionsDisplay(el("bonus-question-qcm-options"), msg.bonus);
      showScreen("screen-bonus-question");
      if (!msg.paused && timerEndAt !== null) startLocalTimer(el("bonus-question-timer-fill"), el("bonus-question-timer-seconds"));
      else stopLocalTimer(el("bonus-question-timer-fill"), el("bonus-question-timer-seconds"));
      break;

    case "bonus-result": {
      el("bonus-result-title").textContent = `Résultat de la question bonus${msg.reveal?.serieLabel ?? ""}`;
      const bonusRevealCible = libelleReveal(msg.reveal);
      el("bonus-reveal-title").textContent = bonusRevealCible.titre;
      el("bonus-reveal-artist").textContent = bonusRevealCible.sousTitre;
      afficherCover(el("bonus-reveal-cover"), el("bonus-reveal-cover-placeholder"), msg.serverBaseUrl, msg.reveal?.coverPath);
      renderResultBadges(el("bonus-reveal-results"), msg.reveal?.resultats ?? [], msg.players ?? []);
      showScreen("screen-bonus-result");
      stopLocalTimer(null);
      break;
    }

    case "ended":
      if (msg.scores) {
        renderScoreList(el("final-scores"), msg.scores);
        renderScoreChart(el("score-chart"), msg.scoreHistory, msg.scores, msg.players, msg.equipes);
      }
      showScreen("screen-ended");
      stopLocalTimer(null);
      break;

    default:
      showScreen("screen-idle");
      stopLocalTimer(null);
  }
}

window.addEventListener("message", (event) => {
  const msg = event.data;
  if (!msg || typeof msg !== "object") return;

  if (msg.type === "state") {
    appliquerEtat(msg);
  } else if (msg.type === "leaderboard-show") {
    renderScoreList(el("leaderboard-scores"), msg.scores);
    el("leaderboard-overlay").classList.remove("hidden");
  } else if (msg.type === "leaderboard-hide") {
    el("leaderboard-overlay").classList.add("hidden");
  }
});

// Demande un rattrapage d'état au panneau de contrôle (utile si cet écran est ouvert/rechargé
// après que la partie a déjà commencé).
if (window.opener) {
  window.opener.postMessage({ type: "request-sync" }, "*");
}

// Retour utilisateur : lancer le blindtest directement depuis l'écran public plutôt que de devoir
// switcher vers le panneau de contrôle — relayé via postMessage, voir app.js:"start-round".
el("btn-launch-from-display")?.addEventListener("click", () => {
  if (window.opener) window.opener.postMessage({ type: "start-round" }, "*");
});

showScreen("screen-idle");
