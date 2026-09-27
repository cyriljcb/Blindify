"use strict";

// État → DOM, panneau de contrôle (docs/refactor-decisions.md section 2). Seul fichier (avec
// audio.js/timers.js) autorisé à lire le DOM directement en dehors des formulaires locaux de
// config.js. `render(state)` est le seul point d'entrée appelé après chaque notify() — chaque
// fonction de rendu par écran repeint tout son contenu depuis `state`, jamais de mise à jour
// incrémentale ciblée par événement (un seul chemin entre "l'état a changé" et "l'écran est à jour").

import { escapeHtml, libelleCible, libelleMode, libelleReveal, lettreSerie, questionCible } from "./shared/format.js";
import { avatarHtml, renderScoreList, renderScoreChart, renderJoinQrCode, renderTitrePanel } from "./shared/components.js";

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
  "screen-serie-intro",
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

// Signalement en direct (V2, section 12.4) — catalogue figé selon RaisonSignalement côté backend
// (Blindify.Domain.Enums), dupliqué ici comme le reste du contrat (voir CLAUDE.md — pas de génération
// de code entre les trois clients).
const RAISONS_SIGNALEMENT = [
  { code: "PasSaPlace", libelle: "N'a rien à faire dans le catalogue" },
  { code: "MauvaiseVersion", libelle: "Mauvaise version (live, remix, reprise...)" },
  { code: "AudioDefectueux", libelle: "Audio défectueux (coupure, volume, qualité...)" },
  { code: "MetadonneesFausses", libelle: "Métadonnées fausses (titre, artiste, année...)" },
  { code: "HorsTheme", libelle: "Hors thème de la série" },
  { code: "RefrainMalPlace", libelle: "Refrain mal placé" },
  { code: "Autre", libelle: "Autre" },
];

// Exporté : utilisé aussi par config.js pour les écrans "connect"/"setup", hors du flux réactif
// (formulaires locaux, rien à refléter depuis l'état serveur — voir docs/refactor-decisions.md
// section 2, inputs de configuration exclus du re-render).
export function showScreen(id) {
  gameControlsEl.classList.toggle("hidden", !ECRANS_AVEC_CONTROLES.has(id));
  // Refonte UI (lot 2) : mise en page « régie » en trois zones pendant la partie (style.css).
  document.body.classList.toggle("regie", ECRANS_AVEC_CONTROLES.has(id));

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

// Refonte UI (lot 1) — « 5/8 ont répondu » + grille des joueurs avec leur temps de réponse.
// Uniquement « a répondu » / « réfléchit » / « déconnecté », jamais juste ou faux ni les scores :
// le host joue souvent (voir la revue UX).
function renderAnswersLive(container, state) {
  const temps = new Map(state.repondants.map((r) => [r.playerId, r.tempsEcouleMs]));
  const cartes = state.players
    .map((p) => {
      const t = temps.get(p.playerId);
      const joker = state.jokersRound.includes(p.playerId) ? " · joker" : "";
      const etat = t !== undefined ? "done" : p.estConnecte ? "wait" : "off";
      const statut =
        etat === "done" ? `✓ ${(t / 1000).toFixed(1)} s${joker}` : etat === "wait" ? `réfléchit…${joker}` : "déconnecté";
      return `<li class="answer-card answer-card--${etat}">${avatarHtml(p.playerId, p.nom, undefined, roster(state))}<div><div class="answer-card__nom">${escapeHtml(p.nom)}</div><div class="answer-card__statut">${statut}</div></div></li>`;
    })
    .join("");
  container.innerHTML = `
    <p class="answers-live__compteur"><strong>${state.players.filter((p) => temps.has(p.playerId)).length}</strong><span> / ${state.players.length}</span> ont répondu</p>
    <ul class="answers-live__grille">${cartes}</ul>`;
}

function renderRound(state) {
  el("round-error").textContent = "";
  const { mode, cible } = state.currentRoundInfo;
  // Refonte UI (lot 2) : la série et le numéro de round sont dans la barre d'étapes.
  el("round-mode-label").textContent = `${questionCible(cible)} · ${libelleMode(mode)}`;
  renderAnswersLive(el("round-answers"), state);
  const connectes = state.players.filter((p) => p.estConnecte);
  const tousOntRepondu = connectes.length > 0 && connectes.every((p) => state.repondants.some((r) => r.playerId === p.playerId));
  el("btn-reveler").disabled = !tousOntRepondu || state.jeuEnPause;
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
  el("bonus-question-mode-label").textContent = `${libelleMode(mode)} — trouver ${libelleCible(cible)}`;
  el("bonus-course-banner").classList.toggle("hidden", !estCourse);
  el("screen-bonus-question").classList.toggle("screen--course", !!estCourse);
  renderAnswersLive(el("bonus-question-answers"), state);
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
  renderTitrePanel(
    {
      panel: el("titre-panel"),
      compteur: el("titre-compteur"),
      libelle: el("titre-libelle"),
      description: el("titre-description"),
      joueurs: el("titre-joueurs"),
    },
    state.currentTitresInfo,
    state.titreIndexAffiche,
    roster(state)
  );
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

function renderFlagsPanel(state) {
  const panel = el("flags-panel");
  const morceaux = state.morceauxJoues;
  panel.classList.toggle("hidden", morceaux.length === 0);
  if (morceaux.length === 0) return;

  el("flags-count").textContent = String(morceaux.length);

  const list = el("flags-list");
  // Reconstruit seulement si le nombre de lignes a changé — évite d'effacer la saisie en cours
  // (raison/commentaire) d'une ligne existante à chaque re-render déclenché par un événement sans
  // rapport (ScoreUpdate, etc. — voir render(), appelé après CHAQUE notify()).
  if (list.children.length === morceaux.length) return;

  list.innerHTML = [...morceaux]
    .reverse() // plus récent d'abord
    .map(
      (m) => `
    <li class="flags-list-item" data-track-id="${escapeHtml(m.trackId)}">
      <div class="flags-list-item__info"><strong>${escapeHtml(m.titre)}</strong> — ${escapeHtml(m.artiste)}</div>
      <div class="flags-list-item__form">
        <select class="flags-raison">
          ${RAISONS_SIGNALEMENT.map((r) => `<option value="${r.code}">${escapeHtml(r.libelle)}</option>`).join("")}
        </select>
        <input type="text" class="flags-commentaire" placeholder="Commentaire (facultatif)" />
        <button type="button" class="flags-submit">Signaler</button>
        <span class="flags-status"></span>
      </div>
    </li>`
    )
    .join("");
}

export function render(state) {
  if (!state.leaderboardOpen) {
    el("leaderboard-overlay").classList.add("hidden");
  } else {
    renderScoreList(el("leaderboard-scores"), state.lastLeaderboardDto, roster(state), { medailles: true });
    el("leaderboard-overlay").classList.remove("hidden");
  }

  renderFlagsPanel(state);

  if (!state.currentScreen) return; // encore sur connect/setup, piloté par config.js

  el("btn-pause").classList.toggle("hidden", state.jeuEnPause);
  el("btn-resume").classList.toggle("hidden", !state.jeuEnPause);

  showScreen(SCREEN_ID[state.currentScreen]);
  RENDERERS[state.currentScreen]?.(state);
  renderRegie(state);
}

// ----- Régie (refonte UI, lot 2) -----

// Action suivante de chaque écran — déclenchée par Espace (main.js) et reproduite en tête de la
// colonne d'actions de la régie (renderRegie) : même bouton, même place, quel que soit l'écran.
export const ACTION_SUIVANTE_PAR_ECRAN = {
  lobby: "btn-start-round",
  "serie-intro": "btn-start-serie",
  round: "btn-reveler",
  "round-ended": "btn-next-round",
  "bonus-result": "btn-end-now",
};

// Libellé de l'action principale quand l'écran n'en a pas (mise et question bonus : la phase se
// termine seule au bout du chrono).
const ATTENTE_PAR_ECRAN = {
  "bonus-stake": "Mises en cours…",
  "bonus-question": "Question bonus en cours…",
};

function renderRegie(state) {
  renderStepper(state);

  const connectes = state.players.filter((p) => p.estConnecte).length;
  const joueursEl = el("players-indicator");
  joueursEl.classList.toggle("hidden", !state.gameCode);
  joueursEl.textContent = `${connectes} joueur${connectes > 1 ? "s" : ""}`;
  joueursEl.classList.toggle("pill--on", connectes > 0);
  joueursEl.classList.toggle("pill--off", connectes === 0);

  // Miroir de l'action principale de l'écran courant : libellé et état repris du bouton d'origine
  // (masqué en mode régie, voir style.css .action-principale), clic relayé par main.js.
  const principale = el("btn-action-principale");
  const cible = el(ACTION_SUIVANTE_PAR_ECRAN[state.currentScreen] ?? "");
  if (cible) {
    principale.textContent = cible.textContent;
    principale.disabled = cible.disabled;
    principale.dataset.kbd = "Espace";
  } else {
    principale.textContent = ATTENTE_PAR_ECRAN[state.currentScreen] ?? "—";
    principale.disabled = true;
    delete principale.dataset.kbd;
  }
}

// Salon › Série A › Série B · round 4/10 › … › Fin. serieCouranteIndex est déjà incrémenté au
// BonusResult (voir handlers.js:onBonusResult) : sur l'écran de résultat bonus, la série en cours
// est donc la précédente.
function renderStepper(state) {
  const stepper = el("stepper");
  const visible = !!state.gameCode && !!state.currentScreen && state.nombreSeriesTotal > 0;
  stepper.classList.toggle("hidden", !visible);
  if (!visible) return;

  const ecran = state.currentScreen;
  const serieEnCours = ecran === "bonus-result" ? state.serieCouranteIndex - 1 : state.serieCouranteIndex;
  const detail = {
    "serie-intro": "annonce",
    round: `round ${state.roundsDemarres}/${state.nombreRoundsParSerie}`,
    "round-ended": `round ${state.roundsDemarres}/${state.nombreRoundsParSerie}`,
    "bonus-stake": "question bonus",
    "bonus-question": "question bonus",
    "bonus-result": "question bonus",
  }[ecran];

  const etapes = [{ libelle: "Salon", etat: ecran === "lobby" ? "now" : "done" }];
  for (let i = 0; i < state.nombreSeriesTotal; i++) {
    let etat = "todo";
    let libelle = `Série ${lettreSerie(i)}`;
    if (ecran === "ended" || (ecran !== "lobby" && i < serieEnCours)) etat = "done";
    else if (ecran !== "lobby" && i === serieEnCours) {
      etat = "now";
      if (detail) libelle += ` · ${detail}`;
    }
    etapes.push({ libelle, etat });
  }
  etapes.push({ libelle: "Fin", etat: ecran === "ended" ? "now" : "todo" });

  stepper.innerHTML = etapes
    .map((e) => `<li class="step step--${e.etat}"${e.etat === "now" ? ' aria-current="step"' : ""}>${escapeHtml(e.libelle)}${e.etat === "done" ? " ✓" : ""}</li>`)
    .join("");
}
