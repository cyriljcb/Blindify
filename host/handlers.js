"use strict";

// Un handler par événement du contrat SignalR (docs/architecture.md section 10) — chacun mute
// `state`, rien d'autre (docs/refactor-decisions.md section 2). Les effets de bord qui ne sont pas
// de la mutation d'état pure (audio, minuteur visuel, écran public) sont câblés depuis main.js, qui
// appelle ces fonctions puis notify() lui-même. handlers.js reste le seul fichier qui connaît le
// vocabulaire serveur (noms d'événements, formes de payload).

import { libelleSerie, libelleTheme, lettreSerie } from "./shared/format.js";

export function onPlayerJoined(state, { playerId, nom }) {
  state.players.push({ playerId, nom, estConnecte: true });
}

export function onPlayerReconnected(state, { playerId }) {
  const p = state.players.find((j) => j.playerId === playerId);
  if (p) p.estConnecte = true;
}

export function onPlayerDisconnected(state, { playerId }) {
  const p = state.players.find((j) => j.playerId === playerId);
  if (p) p.estConnecte = false;
}

export function onPlayerTeamChanged(state, { playerId, teamId }) {
  state.equipeParJoueur[playerId] = teamId;
}

// Diffusé avant le premier round de chaque série (y compris la première) — voir
// GameHub.AnnoncerSerieCourante. Depuis docs/refactor-decisions.md section 1, le serveur calcule
// seul la répartition des thèmes par série (SeriesPlanner.AssignerThemesAuxSeries) : le host ne la
// précalcule plus à la configuration, il l'apprend série par série via cet événement.
export function onSerieAnnoncee(state, { serieIndex, tags }) {
  state.tagsParSerieCourante[serieIndex] = tags;
  state.currentScreen = "serie-intro";
  state.currentDisplayScreen = "serie-intro";
  state.currentSerieIntroInfo = { lettre: lettreSerie(serieIndex), theme: libelleTheme(tags) };
}

export function onRoundStarted(state, payload) {
  state.dernierModeRound = payload.mode;
  state.refrainCourantMs = payload.refrainStartMs ?? null;
  state.roundsDemarres++;
  state.currentScreen = "round";
  state.currentDisplayScreen = "round";
  // qcmOptions : retour utilisateur — affiche aussi les choix sur l'écran public en mode QCM
  // (undefined pour les autres modes, voir render.js:renderQcmOptionsDisplay côté display.js).
  state.currentRoundInfo = {
    mode: payload.mode,
    cible: payload.cible,
    serieLabel: libelleSerie(state.serieCouranteIndex, state.nombreSeriesTotal, state.tagsParSerieCourante),
    qcmOptions: payload.qcmOptions,
  };
}

export function onScoreUpdate(state, dto) {
  // Mémorisé pour le graphique de fin de partie (voir RoundEnded/BonusResult) — ScoreUpdate est
  // diffusé à chaque réponse individuelle. Capturé pour le point d'historique en attente s'il y en
  // a un — RoundEnded/BonusResult sont TOUJOURS suivis d'un ScoreUpdate côté serveur (jamais
  // l'inverse, voir RoundTimerCoordinator/BonusTimerCoordinator), donc c'est ici, sur ce
  // ScoreUpdate qui suit, qu'il faut enregistrer le score à jour.
  state.dernierScoreDto = dto;
  if (state.historiqueLabelEnAttente) {
    state.scoreHistory.push({ label: state.historiqueLabelEnAttente, joueurs: dto.joueurs, equipes: dto.equipes });
    state.historiqueLabelEnAttente = null;
  }
}

export function onRoundEnded(state, payload) {
  state.dernierResultats = payload.resultats;
  state.currentScreen = "round-ended";
  state.currentDisplayScreen = "round-ended";
  state.currentRevealInfo = {
    title: payload.title,
    artist: payload.artist,
    cible: payload.cible,
    film: payload.film,
    coverPath: payload.coverPath,
    resultats: state.dernierResultats.map((r) => ({ playerId: r.playerId, estCorrecte: r.estCorrecte })),
  };
}

export function onGamePaused(state) {
  state.jeuEnPause = true;
}

export function onGameResumed(state) {
  state.jeuEnPause = false;
}

export function onLeaderboardShown(state, dto) {
  state.leaderboardOpen = true;
  state.lastLeaderboardDto = dto;
}

export function onGameEnded(state, dto) {
  state.currentScreen = "ended";
  state.currentDisplayScreen = "ended";
  state.currentScoresInfo = dto;
}

export function onGameRestarted(state) {
  state.roundsDemarres = 0;
  state.serieCouranteIndex = 0;
  state.scoreHistory = [];
  state.dernierScoreDto = null;
  state.historiqueLabelEnAttente = null;
  // RejouerPartie (backend) reconstruit SeriesList à partir de la configuration précédente (mêmes
  // séries/tags, nouvelle sélection de morceaux) — la partie reste donc immédiatement démarrable,
  // pas besoin de repasser par ConfigurerPartie.
  state.partieConfiguree = true;
  state.currentScreen = "lobby";
  state.currentDisplayScreen = "lobby";
  state.currentRoundInfo = {};
  state.currentRevealInfo = {};
  state.currentScoresInfo = null;
}

export function onBonusStakeOptions(state, payload) {
  state.currentScreen = "bonus-stake";
  state.currentDisplayScreen = "bonus-stake";
  state.currentBonusInfo = {
    paliers: payload.paliers,
    serieLabel: libelleSerie(state.serieCouranteIndex, state.nombreSeriesTotal, state.tagsParSerieCourante),
  };
}

export function onBonusQuestionStarted(state, payload) {
  // Seul le host reçoit filePath/refrainStartMs/ralentissement (jamais envoyés aux joueurs).
  state.refrainCourantMs = payload.refrainStartMs ?? null;
  state.currentScreen = "bonus-question";
  state.currentDisplayScreen = "bonus-question";
  // cible/qcmOptions : Mode tiré aléatoirement comme un round classique (QCM/Première lettre en
  // plus de la réponse tapée), qcmOptions affiché sur l'écran public en mode QCM comme pour un
  // round classique.
  state.currentBonusInfo = {
    ralenti: payload.ralentissementActive,
    serieLabel: libelleSerie(state.serieCouranteIndex, state.nombreSeriesTotal, state.tagsParSerieCourante),
    mode: payload.mode,
    cible: payload.cible,
    qcmOptions: payload.qcmOptions,
    estCourse: payload.estCourse,
  };
}

// Question bonus = fin de la série courante — enchaîne sur la série suivante s'il en reste une,
// sinon termine la partie (orchestré depuis main.js, qui lit state.serieCouranteIndex/nombreSeriesTotal
// après cet appel pour choisir entre les deux).
export function onBonusResult(state, payload) {
  // Capturé AVANT l'incrément de serieCouranteIndex plus bas : ce résultat concerne la série qui
  // vient de se terminer, pas la suivante.
  const labelSerieTerminee = libelleSerie(state.serieCouranteIndex, state.nombreSeriesTotal, state.tagsParSerieCourante);

  // Un seul point par série (pas un par round) : retour utilisateur — la question bonus marque la
  // fin de la série, c'est le seul moment où on capture le score sur le graphique.
  state.historiqueLabelEnAttente = `Série ${lettreSerie(state.serieCouranteIndex)}`;

  state.serieCouranteIndex++;

  state.currentScreen = "bonus-result";
  state.currentDisplayScreen = "bonus-result";
  state.currentRevealInfo = {
    title: payload.title,
    artist: payload.artist,
    cible: payload.cible,
    film: payload.film,
    coverPath: payload.coverPath,
    serieLabel: labelSerieTerminee,
    resultats: payload.resultats.map((r) => ({ playerId: r.playerId, estCorrecte: r.estCorrecte })),
  };
}

export function registerHandlers(connection, callbacks) {
  connection.onclose((err) => {
    console.error("SignalR onclose:", err);
    callbacks.onConnectionChanged(false);
  });
  connection.onreconnecting((err) => {
    console.warn("SignalR onreconnecting:", err);
    callbacks.onConnectionChanged(false);
  });
  connection.onreconnected(() => callbacks.onConnectionChanged(true));

  connection.on("PlayerJoined", (payload) => callbacks.onEvent("PlayerJoined", payload));
  connection.on("PlayerReconnected", (payload) => callbacks.onEvent("PlayerReconnected", payload));
  connection.on("PlayerDisconnected", (payload) => callbacks.onEvent("PlayerDisconnected", payload));
  connection.on("PlayerTeamChanged", (payload) => callbacks.onEvent("PlayerTeamChanged", payload));
  connection.on("SerieAnnoncee", (payload) => callbacks.onEvent("SerieAnnoncee", payload));
  connection.on("RoundStarted", (payload) => callbacks.onEvent("RoundStarted", payload));
  connection.on("ScoreUpdate", (payload) => callbacks.onEvent("ScoreUpdate", payload));
  connection.on("PlayerAnswered", (payload) => callbacks.onEvent("PlayerAnswered", payload));
  connection.on("RoundEnded", (payload) => callbacks.onEvent("RoundEnded", payload));
  connection.on("GamePaused", () => callbacks.onEvent("GamePaused"));
  connection.on("GameResumed", () => callbacks.onEvent("GameResumed"));
  connection.on("LeaderboardShown", (payload) => callbacks.onEvent("LeaderboardShown", payload));
  connection.on("GameEnded", (payload) => callbacks.onEvent("GameEnded", payload));
  connection.on("GameRestarted", () => callbacks.onEvent("GameRestarted"));
  connection.on("BonusStakeOptions", (payload) => callbacks.onEvent("BonusStakeOptions", payload));
  connection.on("BonusQuestionStarted", (payload) => callbacks.onEvent("BonusQuestionStarted", payload));
  connection.on("BonusResult", (payload) => callbacks.onEvent("BonusResult", payload));
}

// Table de dispatch utilisée par main.js — associe chaque nom d'événement à sa fonction de mutation
// d'état pure ci-dessus.
export const mutations = {
  PlayerJoined: onPlayerJoined,
  PlayerReconnected: onPlayerReconnected,
  PlayerDisconnected: onPlayerDisconnected,
  PlayerTeamChanged: onPlayerTeamChanged,
  SerieAnnoncee: onSerieAnnoncee,
  RoundStarted: onRoundStarted,
  ScoreUpdate: onScoreUpdate,
  RoundEnded: onRoundEnded,
  GamePaused: onGamePaused,
  GameResumed: onGameResumed,
  LeaderboardShown: onLeaderboardShown,
  GameEnded: onGameEnded,
  GameRestarted: onGameRestarted,
  BonusStakeOptions: onBonusStakeOptions,
  BonusQuestionStarted: onBonusQuestionStarted,
  BonusResult: onBonusResult,
};
