"use strict";

// État unique du panneau de contrôle — remplace les ~25 variables globales éparses de l'ancien
// app.js monolithique (voir docs/refactor-decisions.md section 2). handlers.js mute cet objet et
// appelle notify() ; c'est le seul chemin entre "l'état a changé" et "l'écran est à jour" (render.js
// et display-bridge.js sont les deux seuls abonnés, câblés depuis main.js).

export const state = {
  // ----- Connexion -----
  serverBaseUrl: "",
  connected: false,

  // ----- Partie -----
  gameCode: null,
  // Distinct du code de partie (public) — requis par RejoinAsHost, jamais envoyé aux joueurs.
  hostSecret: null,
  players: [], // { playerId, nom, estConnecte }
  equipes: [], // [{ id, nom }] — vide si mode équipe inactif
  equipeParJoueur: {}, // playerId -> teamId

  // ----- Configuration / cycle de vie -----
  // "Démarrer la partie" reste désactivé tant qu'une configuration n'a pas été validée avec succès
  // au moins une fois pour CE lobby (retour utilisateur du 2026-08-24 — CreateGame ne configure
  // plus le blindtest, voir ConfigurerPartie).
  partieConfiguree: false,
  nombreRoundsParSerie: 0, // identique pour chaque série (champ partagé, voir config.js)
  nombreSeriesTotal: 0,
  serieCouranteIndex: 0,
  roundsDemarres: 0, // rounds démarrés DANS LA SÉRIE COURANTE — remis à 0 à chaque changement de série
  tagsDisponibles: [], // thèmes connus du catalogue (GET /api/tags), pour les cases à cocher
  // Thèmes réellement assignés à chaque série (index -> liste de tags), reçus série par série via
  // l'événement SerieAnnoncee (le serveur calcule désormais la répartition — voir
  // docs/refactor-decisions.md section 1) plutôt que précalculés côté client à la configuration.
  tagsParSerieCourante: [],

  // ----- Round classique -----
  dernierModeRound: null,
  dernierResultats: [],
  refrainCourantMs: null,

  // ----- Historique des scores (graphique de fin de partie) -----
  // Un seul point par SÉRIE résolue (capturé à la question bonus, qui marque la fin de série) —
  // retour utilisateur : pas un point par round, trop granulaire pour "qui menait quand".
  scoreHistory: [],
  dernierScoreDto: null,
  historiqueLabelEnAttente: null, // label du prochain ScoreUpdate à enregistrer

  // ----- Pause -----
  jeuEnPause: false,

  // ----- Tableau général -----
  leaderboardOpen: false,
  lastLeaderboardDto: null,

  // ----- Écran actif du panneau de contrôle -----
  // "connect"/"setup" restent hors de ce champ : ces deux écrans sont pilotés directement par
  // config.js/main.js (formulaires locaux, rien à refléter depuis l'état serveur) — voir
  // docs/refactor-decisions.md section 2 (inputs de configuration exclus du re-render).
  currentScreen: null,

  // ----- Écran public (diffusé par postMessage, voir display-bridge.js) -----
  currentDisplayScreen: "idle",
  currentRoundInfo: {},
  currentRevealInfo: {},
  currentBonusInfo: {},
  currentScoresInfo: null,
  currentSerieIntroInfo: {},
};

const listeners = new Set();

export function subscribe(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

export function notify() {
  for (const fn of listeners) fn(state);
}
