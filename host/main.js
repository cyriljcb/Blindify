"use strict";

// Bootstrap et câblage — orchestre tous les autres modules (docs/refactor-decisions.md section 2).
// Point d'entrée unique chargé par host/index.html (`<script type="module" src="main.js">`).

import { state, notify, subscribe, loadHostSession, clearHostSession } from "./state.js";
import * as transport from "./transport.js";
import { registerHandlers, mutations } from "./handlers.js";
import { render, setConnected, showScreen, ACTION_SUIVANTE_PAR_ECRAN } from "./render.js";
import * as audio from "./audio.js";
import * as timers from "./timers.js";
import * as displayBridge from "./display-bridge.js";
import { chargerTagsDisponibles, chargerPresets } from "./config.js";

const el = (id) => document.getElementById(id);

// Écran host censé rester allumé toute la soirée (contrôle du jeu + lecture audio) — best-effort,
// support Screen Wake Lock API pas garanti sur tous les navigateurs desktop (voir docs/architecture.md
// "Écran toujours allumé"). Jamais bloquant si l'API est absente ou refuse (onglet en arrière-plan).
if ("wakeLock" in navigator) {
  navigator.wakeLock.request("screen").catch(() => {});
}

subscribe(render);
subscribe(displayBridge.syncDisplay);
subscribe(publierEtatRegieSiChange);

// ----- Connexion -----

const SERVER_URL_STORAGE_KEY = "blindify_host_server_url";

// [timeoutMs] borne la tentative (utilisé pour la reconnexion auto au démarrage) ; sans borne pour
// un clic manuel sur "Se connecter". [silencieux] masque le message d'erreur en cas d'échec — évite
// une alerte pour une tentative que l'utilisateur n'a pas déclenchée lui-même.
async function tenterConnexion(url, { silencieux = false, timeoutMs = null } = {}) {
  const errorEl = el("connect-error");
  if (!silencieux) errorEl.textContent = "";

  url = url.trim().replace(/\/+$/, "");
  if (!url) {
    if (!silencieux) errorEl.textContent = "Adresse requise (ex. http://192.168.1.42:5000).";
    return false;
  }
  state.serverBaseUrl = url;

  await transport.arreterConnexion();
  const connection = transport.creerConnexion(url);
  registerHandlers(connection, { onConnectionChanged: setConnected, onEvent: handleEvent, onReconnected: reprendreControleApresReconnexion });

  try {
    await transport.demarrerConnexion(timeoutMs);
    setConnected(true);
    localStorage.setItem(SERVER_URL_STORAGE_KEY, state.serverBaseUrl);
    chargerTagsDisponibles(); // best-effort — n'empêche pas de créer une partie si ça échoue
    if (!(await tenterResumeHostSession())) showScreen("screen-setup");
    return true;
  } catch (err) {
    console.error(err);
    if (!silencieux) errorEl.textContent = "Connexion impossible : vérifiez l'adresse et que le serveur tourne.";
    showScreen("screen-connect");
    return false;
  }
}

// Retour utilisateur (2026-11-01) : une reconnexion automatique de SignalR donne une nouvelle
// connexion, que le serveur ne connaît pas comme host (HostConnectionId) ni comme membre du groupe de
// la partie. On se ré-annonce et on se resynchronise sur l'état complet renvoyé (audit 2026-11-10 :
// un RoundEnded/BonusResult manqué pendant une coupure de quelques secondes laissait la TV et le
// panneau figés sur la question).
async function reprendreControleApresReconnexion() {
  if (!state.gameCode || !state.hostSecret) return;
  try {
    const snapshot = await transport.invoke.rejoinAsHost(state.gameCode, state.hostSecret);
    appliquerSnapshot(snapshot, { rechargement: false });
  } catch (err) {
    console.error("RejoinAsHost après reconnexion :", err);
    // Serveur redémarré entre-temps (état en mémoire) : la partie est perdue, le dire clairement.
    erreurEcranCourant("La partie n'existe plus sur le serveur (redémarré ?) — crée un nouveau salon.");
  }
}

// ----- Reprise à partir du snapshot de RejoinAsHost (audit 2026-11-10) -----
// Reconstruit l'écran courant en rejouant les handlers habituels (mêmes mutations, mêmes effets),
// puis recale audio et minuteur sur la position réelle. rechargement=false (simple reconnexion) :
// si la question affichée est toujours celle du serveur, on ne fait que resynchroniser joueurs,
// pause et réponses reçues, sans relancer l'audio ni les minuteurs.
const ECRAN_PAR_PHASE = {
  Lobby: "lobby",
  AvantSerie: "serie-intro",
  Round: "round",
  RoundTermine: "round-ended",
  RoundPret: "round-ended",
  BonusMise: "bonus-stake",
  BonusQuestion: "bonus-question",
  BonusTermine: "bonus-result",
  Termine: "ended",
};

function idQuestionDuSnapshot(snapshot) {
  return snapshot.round?.roundId ?? snapshot.bonusQuestion?.roundId ?? snapshot.bonusMise?.roundId ?? null;
}

function appliquerSnapshot(snapshot, { rechargement }) {
  state.jeuEnPause = snapshot.enPause;
  state.votesThemes = snapshot.votesThemes ?? null;
  state.partieConfiguree = snapshot.partieConfiguree;
  state.nombreSeriesTotal = snapshot.nombreSeries;
  state.nombreRoundsParSerie = snapshot.nombreRoundsParSerie;
  state.tagsParSerieCourante = snapshot.tagsParSerie ?? [];
  state.equipes = snapshot.equipes ?? [];
  state.players = (snapshot.joueurs ?? []).map((j) => ({ playerId: j.playerId, nom: j.nom, estConnecte: j.estConnecte, aQuitte: !!j.aPartie }));
  state.equipeParJoueur = Object.fromEntries((snapshot.joueurs ?? []).filter((j) => j.teamId).map((j) => [j.playerId, j.teamId]));
  if (snapshot.scores) state.dernierScoreDto = snapshot.scores;

  const ecran = ECRAN_PAR_PHASE[snapshot.phase] ?? "lobby";
  const memeQuestion = !rechargement && state.currentScreen === ecran && idQuestionDuSnapshot(snapshot) === state.questionIdCourante
    && snapshot.phase !== "AvantSerie";
  if (memeQuestion) {
    if (snapshot.repondants) state.repondants = snapshot.repondants;
    if (snapshot.misesEnregistrees) state.misesEnregistrees = snapshot.misesEnregistrees;
    notify();
    return;
  }

  timers.annulerTousLesMinuteurs();
  timers.stopTimer();
  state.serieCouranteIndex = snapshot.serieCouranteIndex;
  state.roundSuivantDejaAvance = false;
  displayBridge.resetPlayerAnswered();

  switch (snapshot.phase) {
    case "AvantSerie":
      state.roundsDemarres = 0;
      // Rediffuse l'annonce de la série (SerieAnnoncee pilote l'écran d'intro et son minuteur).
      afficherIntroSerie();
      break;

    case "Round": {
      state.roundsDemarres = Math.max(0, snapshot.roundsDemarresDansSerie - 1);
      mutations.RoundStarted(state, snapshot.round);
      state.repondants = snapshot.repondants ?? [];
      for (const r of state.repondants) displayBridge.sendPlayerAnswered(r);
      audio.resumeAudio(state.serverBaseUrl, snapshot.round.filePath, snapshot.positionAudioMs ?? 0, snapshot.enPause);
      audio.definirRepereRefrain(state.refrainCourantMs);
      timers.startTimer(snapshot.round.dureeFenetreReponseMs, el("timer-fill"), snapshot.tempsEcouleMs ?? 0, snapshot.enPause);
      break;
    }

    case "RoundTermine":
    case "RoundPret":
      state.roundsDemarres = snapshot.roundsDemarresDansSerie;
      mutations.RoundEnded(state, snapshot.roundEnded);
      state.roundSuivantDejaAvance = snapshot.phase === "RoundPret";
      if (!snapshot.enPause) armerEnchainementApresRound();
      break;

    case "BonusMise":
      state.roundsDemarres = snapshot.nombreRoundsParSerie;
      mutations.BonusStakeOptions(state, snapshot.bonusMise);
      state.misesEnregistrees = snapshot.misesEnregistrees ?? [];
      timers.startTimer(snapshot.bonusMise.dureePhaseMiseMs, el("bonus-stake-timer-fill"), snapshot.tempsEcouleMs ?? 0, snapshot.enPause);
      break;

    case "BonusQuestion": {
      state.roundsDemarres = snapshot.nombreRoundsParSerie;
      const q = snapshot.bonusQuestion;
      mutations.BonusQuestionStarted(state, q);
      state.repondants = snapshot.repondants ?? [];
      for (const r of state.repondants) displayBridge.sendPlayerAnswered(r);
      const rate = q.ralentissementActive ? q.facteurRalentissement : 1;
      audio.resumeAudio(state.serverBaseUrl, q.filePath, snapshot.positionAudioMs ?? 0, snapshot.enPause, rate);
      audio.definirRepereRefrain(state.refrainCourantMs);
      timers.startTimer(q.dureePhaseQuestionMs, el("bonus-question-timer-fill"), snapshot.tempsEcouleMs ?? 0, snapshot.enPause);
      break;
    }

    case "BonusTermine":
      state.roundsDemarres = snapshot.nombreRoundsParSerie;
      mutations.BonusResult(state, snapshot.bonusResult);
      if (!snapshot.enPause) armerEnchainementApresBonus(false);
      break;

    case "Termine":
      mutations.GameEnded(state, snapshot.gameEnded);
      demarrerDefilementTitres();
      break;

    default:
      state.currentScreen = "lobby";
      state.currentDisplayScreen = "lobby";
      chargerPresets();
  }

  notify();
}

el("btn-connect").addEventListener("click", () => tenterConnexion(el("server-url").value));

// V2 — reconnexion host : restaure le contrôle d'une partie déjà créée après un refresh accidentel
// de la page (code + hostSecret en sessionStorage, voir state.js). Depuis l'audit 2026-11-10, le
// snapshot de RejoinAsHost porte l'état complet (phase, joueurs, série, question en cours, résultats)
// et l'écran courant est reconstruit tel quel — voir appliquerSnapshot. Retourne true si une session
// a été reprise avec succès.
async function tenterResumeHostSession() {
  const session = loadHostSession();
  if (!session) return false;

  try {
    const snapshot = await transport.invoke.rejoinAsHost(session.code, session.hostSecret);
    state.gameCode = session.code;
    state.hostSecret = session.hostSecret;
    chargerPresets();
    appliquerSnapshot(snapshot, { rechargement: true });
    return true;
  } catch (err) {
    // Secret invalide ou partie disparue (backend redémarré entre-temps, état 100% en mémoire) —
    // pas la peine de réessayer indéfiniment, ni de bloquer la connexion pour autant.
    console.error("RejoinAsHost:", err);
    clearHostSession();
    el("setup-error").textContent = "La partie précédente n'existe plus sur le serveur (redémarré ?) — crée un nouveau salon.";
    return false;
  }
}

// ----- Dispatch des événements SignalR : mutation d'état (handlers.js) + effets de bord (audio,
// minuteur, écran public) + notify(). handlers.js reste le seul fichier qui connaît le vocabulaire
// serveur ; ce switch ne connaît que les EFFETS qui accompagnent chaque mutation. -----

function handleEvent(name, payload) {
  mutations[name]?.(state, payload);

  switch (name) {
    case "SerieAnnoncee":
      // Refonte UI (lot 3) : le classement affiché en fin de série se referme à l'annonce de la
      // suivante (côté téléphones, voir GameConnection : même règle).
      timers.annulerMinuteur("classement-auto");
      if (state.leaderboardOpen) {
        state.leaderboardOpen = false;
        displayBridge.sendLeaderboardHide();
      }
      timers.minuteurAnnulable("auto-intro-serie", DUREE_INTRO_SERIE_MS, demarrerRoundApresIntro, {
        onTick: (restantMs) => {
          el("serie-intro-note").textContent = `Démarrage automatique dans ${Math.ceil(restantMs / 1000)}s...`;
        },
        onCancel: () => { el("serie-intro-note").textContent = ""; },
      });
      break;

    case "RoundStarted":
      audio.playAudio(state.serverBaseUrl, payload.filePath); // toujours depuis le début — le refrain n'est joué qu'au reveal
      audio.definirRepereRefrain(state.refrainCourantMs);
      timers.startTimer(payload.dureeFenetreReponseMs, el("timer-fill"));
      displayBridge.resetPlayerAnswered();
      break;

    case "PlayerAnswered":
      // Réaction visuelle + classement de rapidité sur l'écran public uniquement (retour
      // utilisateur) — jamais sur le téléphone des joueurs, jamais l'exactitude de la réponse
      // (voir PlayerAnsweredDto côté backend).
      displayBridge.sendPlayerAnswered(payload);
      break;

    case "JokerUtilise":
      // V2, section 12.7 — annonce transitoire sur l'écran public uniquement, jamais l'effet ni
      // la cible révélée (voir JokerUtiliseDto côté backend : {playerId} seul).
      displayBridge.sendJokerUtilise(payload);
      break;

    case "RoundEnded":
      timers.stopTimer();
      // Pas de resetPlayerAnswered ici (refonte UI, lot 1) : l'écran public en a besoin pour trier
      // les résultats par rapidité ; vidé au prochain RoundStarted/BonusQuestionStarted.
      // Au reveal (tout le monde a répondu) : on saute au refrain si on en connaît un pour ce
      // morceau, sinon on retombe sur le comportement "musique continue" habituel.
      if (state.refrainCourantMs !== null) {
        audio.jouerRefrain(state.refrainCourantMs);
      } else if (!el("setup-audio-continu").checked) {
        audio.pauseAudioEnDouceur();
      }
      armerEnchainementApresRound();
      break;

    case "GamePaused":
      // Audit 2026-11-10 : seule la musique qui jouait est relancée à la reprise (pas pendant une
      // mise, une annonce de série ou un reveal sans « musique continue »).
      audioJouaitAvantPause = audio.estEnLecture();
      audio.pauseAudioEnDouceur();
      timers.pauseTimer();
      timers.annulerMinuteur("auto-next");
      timers.annulerMinuteur("auto-fin-ou-serie-suivante");
      // Audit 2026-11-10 : ces minuteurs continuaient pendant la pause — un round démarrait en
      // pleine pause après l'annonce de série, ou l'audio du mode course partait quand même.
      timers.annulerMinuteur("auto-intro-serie");
      timers.annulerMinuteur("classement-auto");
      if (introCourseEnAttente) timers.annulerMinuteur("bonus-course-intro");
      break;

    case "GameResumed":
      timers.resumeTimer();
      if (introCourseEnAttente) {
        // Intro du mode course interrompue : l'audio et le chrono démarrent maintenant.
        const demarrer = introCourseEnAttente;
        introCourseEnAttente = null;
        demarrer();
      } else if (audioJouaitAvantPause) {
        audio.lancerLecture();
        audio.fadeAudioVolume(1, 450);
      }
      audioJouaitAvantPause = false;
      break;

    case "GameEnded":
      timers.stopTimer();
      timers.annulerMinuteur("auto-fin-ou-serie-suivante");
      demarrerDefilementTitres();
      break;

    case "GameRestarted":
      timers.annulerMinuteur("auto-intro-serie");
      timers.annulerMinuteur("bonus-course-intro");
      timers.annulerMinuteur("titre-suivant");
      audio.remettreVitesseNormale();
      displayBridge.resetPlayerAnswered();
      break;

    case "BonusStakeOptions":
      timers.startTimer(payload.dureePhaseMiseMs, el("bonus-stake-timer-fill"));
      break;

    case "MiseEnregistree":
      // Retour utilisateur (2026-10-17) — animation « mise verrouillée » sur l'écran public, jamais le
      // palier choisi (voir MiseEnregistreeDto côté backend).
      displayBridge.sendMiseEnregistree(payload);
      break;

    case "BonusQuestionStarted": {
      displayBridge.resetPlayerAnswered();
      // Arrête tout de suite le chrono de la mise (audit 2026-11-10 : la TV l'affichait à 0 s
      // pendant l'intro du mode course).
      timers.stopTimer();
      const demarrerAudioEtMinuteur = () => {
        introCourseEnAttente = null;
        const rate = payload.ralentissementActive ? payload.facteurRalentissement : 1;
        audio.playAudio(state.serverBaseUrl, payload.filePath, rate); // depuis le début — c'est la devinette elle-même
        audio.definirRepereRefrain(state.refrainCourantMs);
        timers.startTimer(payload.dureePhaseQuestionMs, el("bonus-question-timer-fill"));
      };
      // Retour utilisateur : en mode course, l'audio démarrait ici immédiatement alors que l'app
      // joueur force un écran d'intro "Mode course" pendant DUREE_INTRO_COURSE_MS avant de laisser
      // répondre (voir app/lib/services/game_connection.dart:_dureeIntroCourse) — ~2,5s de musique
      // audibles sur l'enceinte du host sans qu'aucun joueur ne puisse encore réagir. Les deux
      // délais doivent rester identiques ; DemarrerAudioEtMinuteur est annulable via
      // annulerMinuteur("bonus-course-intro") si un BonusResult arrive avant l'échéance.
      if (payload.estCourse) {
        introCourseEnAttente = demarrerAudioEtMinuteur;
        timers.minuteurAnnulable("bonus-course-intro", DUREE_INTRO_COURSE_MS, demarrerAudioEtMinuteur);
      } else {
        demarrerAudioEtMinuteur();
      }
      break;
    }

    case "BonusResult":
      timers.annulerMinuteur("bonus-course-intro");
      introCourseEnAttente = null;
      timers.stopTimer();
      audio.remettreVitesseNormale(); // remis à la vitesse normale pour la suite (fin de partie, replay...)
      if (state.refrainCourantMs !== null) {
        audio.jouerRefrain(state.refrainCourantMs);
      } else {
        audio.pauseAudioEnDouceur();
      }
      armerEnchainementApresBonus(true);
      break;

    case "LeaderboardShown":
      displayBridge.sendLeaderboardShow(payload);
      break;

    // Refonte UI (lot 3) — télécommande d'un admin (téléphone), relayée par le serveur : exécutée
    // exactement comme le raccourci clavier correspondant.
    case "CommandeHost":
      if (payload.commande === "suivant") {
        const id = ACTION_SUIVANTE_PAR_ECRAN[state.currentScreen];
        if (id) cliquerSiDisponible(id);
      } else if (payload.commande === "reecouter") {
        cliquerSiDisponible("btn-reecouter");
      }
      break;
  }

  notify();
}

// Minuteur d'enchaînement après un round révélé — partagé entre RoundEnded et la reprise
// (appliquerSnapshot).
function armerEnchainementApresRound() {
  timers.minuteurAnnulable("auto-next", delaiEnchainementMs(), avancerRoundSuivant, {
    onTick: (restantMs) => {
      const label = state.roundsDemarres >= state.nombreRoundsParSerie ? "Question bonus" : "Round suivant";
      el("auto-next-note").textContent = `${label} dans ${Math.ceil(restantMs / 1000)}s...`;
    },
    onCancel: () => { el("auto-next-note").textContent = ""; },
  });
}

// Après le résultat d'une question bonus : série suivante ou fin de partie. [avecClassement] :
// classement automatique aux séries prévues (pas lors d'une reprise, pour ne pas le rejouer).
function armerEnchainementApresBonus(avecClassement) {
  // Refonte UI (lot 3) : classement automatique (TV + téléphones) aux séries prévues, quelques
  // secondes après le résultat ; la série suivante attend d'autant plus longtemps.
  const classementAuto = avecClassement && seriesAvecClassementAuto(state.nombreSeriesTotal).includes(state.serieCouranteIndex);
  if (classementAuto) {
    timers.minuteurAnnulable("classement-auto", DELAI_AVANT_CLASSEMENT_AUTO_MS, () =>
      transport.invoke.showLeaderboard().catch((err) => console.error("Classement automatique :", err))
    );
  }
  if (state.serieCouranteIndex < state.nombreSeriesTotal) {
    const delaiSerieSuivante = delaiEnchainementMs() + (classementAuto ? DUREE_CLASSEMENT_AUTO_MS : 0);
    timers.minuteurAnnulable("auto-fin-ou-serie-suivante", delaiSerieSuivante, avancerSerieSuivante, {
      onTick: (restantMs) => {
        el("bonus-end-note").textContent = `Série suivante dans ${Math.ceil(restantMs / 1000)}s...`;
      },
      onCancel: () => { el("bonus-end-note").textContent = ""; },
    });
  } else {
    timers.minuteurAnnulable("auto-fin-ou-serie-suivante", delaiEnchainementMs(), () => {
      transport.invoke.endGame().catch((err) => {
        console.error(err);
        el("bonus-end-note").textContent = "Erreur : " + (err.message || err);
      });
    }, {
      onTick: (restantMs) => {
        el("bonus-end-note").textContent = `Fin de la partie dans ${Math.ceil(restantMs / 1000)}s...`;
      },
      onCancel: () => { el("bonus-end-note").textContent = ""; },
    });
  }
}

// État de pause local (voir GamePaused/GameResumed).
let audioJouaitAvantPause = false;
let introCourseEnAttente = null;

audio.definirSurErreurAudio(() => {
  erreurEcranCourant("Fichier audio introuvable ou illisible pour ce morceau — passe au suivant et signale-le.");
});

function delaiEnchainementMs() {
  return Math.max(1, parseInt(el("setup-delai-enchainement").value, 10) || 10) * 1000;
}

// ----- Classement automatique (refonte UI, lot 3) -----
// Choix utilisateur : moins de SEUIL_DOUBLE_CLASSEMENT séries → une fois, à la moitié ; au-delà →
// deux fois, au tiers et aux deux tiers. Jamais après la dernière série (l'écran de fin montre déjà
// les scores). Valeur retournée : numéros (à partir de 1) des séries après lesquelles l'afficher.
const SEUIL_DOUBLE_CLASSEMENT = 7;
const DELAI_AVANT_CLASSEMENT_AUTO_MS = 3000;
const DUREE_CLASSEMENT_AUTO_MS = 10000;

function seriesAvecClassementAuto(nombreSeries) {
  const moments = nombreSeries < SEUIL_DOUBLE_CLASSEMENT
    ? [Math.ceil(nombreSeries / 2)]
    : [Math.round(nombreSeries / 3), Math.round((2 * nombreSeries) / 3)];
  return [...new Set(moments)].filter((s) => s >= 1 && s < nombreSeries);
}

// ----- Enchaînement automatique -----

// Vérifié côté client avant d'appeler le serveur : la série classique EN COURS est épuisée, on
// enchaîne directement sur SA question bonus plutôt que d'attendre une intervention. Ne jamais
// appeler NextRound() ici dans ce cas précis : passé le dernier round d'une série, NextRound()
// avancerait tout seul à la série suivante côté backend (voir GameHub.NextRound) et sauterait
// silencieusement la question bonus de la série qu'on vient de terminer.
async function avancerRoundSuivant() {
  el("round-ended-note").textContent = "";

  if (state.roundsDemarres >= state.nombreRoundsParSerie) {
    try {
      await transport.invoke.startBonusRound();
    } catch (err) {
      console.error(err);
      el("round-ended-note").textContent = "Erreur au démarrage de la question bonus : " + (err.message || err);
    }
    return;
  }

  try {
    // Reprise après rechargement (phase RoundPret) : NextRound a déjà été appelé côté serveur.
    if (!state.roundSuivantDejaAvance) await transport.invoke.nextRound();
    state.roundSuivantDejaAvance = false;
    await transport.invoke.startRound();
  } catch (err) {
    console.error(err);
    el("round-ended-note").textContent = "Erreur : " + (err.message || err);
  }
}

// Bascule effectivement sur la série suivante (NextRound() franchit la frontière de série côté
// serveur puisque RoundCourantIndex est déjà au dernier index — voir GameHub.NextRound) puis
// annonce la nouvelle série — c'est le handler SerieAnnoncee qui affichera l'écran d'intro et
// démarrera son minuteur, pas cette fonction.
async function avancerSerieSuivante() {
  state.roundsDemarres = 0; // nouvelle série : le compteur "rounds démarrés dans la série courante" repart de 0
  try {
    await transport.invoke.nextRound();
    await afficherIntroSerie();
  } catch (err) {
    console.error(err);
    el("bonus-end-note").textContent = "Erreur au démarrage de la série suivante : " + (err.message || err);
  }
}

const DUREE_INTRO_SERIE_MS = 5000;

// Doit rester identique à app/lib/services/game_connection.dart:_dureeIntroCourse — voir le
// commentaire sur le case "BonusQuestionStarted" ci-dessus.
const DUREE_INTRO_COURSE_MS = 2500;

// Affiché avant le premier round de la série courante — panneau host, écran public ET l'app joueur
// (AnnoncerSerieCourante diffuse la même annonce aux téléphones, voir GameHub.cs). Le rendu de
// l'écran d'intro et son minuteur d'enchaînement sont déclenchés par le handler SerieAnnoncee (voir
// handleEvent), pas ici directement — depuis docs/refactor-decisions.md section 1, le serveur est
// seul à connaître les tags de la série (SeriesPlanner.AssignerThemesAuxSeries).
async function afficherIntroSerie() {
  try {
    await transport.invoke.annoncerSerieCourante();
  } catch (err) {
    console.error("AnnoncerSerieCourante:", err);
  }
}

function demarrerRoundApresIntro() {
  timers.annulerMinuteur("auto-intro-serie");
  transport.invoke.startRound().catch((err) => {
    console.error(err);
    el("serie-intro-error").textContent = "Erreur : " + (err.message || err);
  });
}

el("btn-start-serie").addEventListener("click", demarrerRoundApresIntro);

// ----- Défilement séquentiel des titres de fin de partie (V2, section 12.6) -----
// ~4 s/titre, interruptible par le host via btn-titre-suivant (avance immédiatement le décompte).

const DUREE_AFFICHAGE_TITRE_MS = 4000;

function demarrerDefilementTitres() {
  if (!state.currentTitresInfo || state.currentTitresInfo.length === 0) return;
  state.titreIndexAffiche = 0;
  notify();
  programmerTitreSuivant();
}

function programmerTitreSuivant() {
  timers.minuteurAnnulable("titre-suivant", DUREE_AFFICHAGE_TITRE_MS, avancerTitre);
}

function avancerTitre() {
  const suivant = state.titreIndexAffiche + 1;
  if (suivant >= state.currentTitresInfo.length) {
    state.titreIndexAffiche = -1; // dernier titre déjà montré : masque le panneau
    notify();
    return;
  }
  state.titreIndexAffiche = suivant;
  notify();
  programmerTitreSuivant();
}

el("btn-titre-suivant").addEventListener("click", () => {
  timers.annulerMinuteur("titre-suivant");
  avancerTitre();
});

// Extrait pour être aussi déclenchable depuis l'écran public (retour utilisateur : pénible de
// switcher vers le panneau de contrôle juste pour lancer — voir display-bridge.js:"start-round").
function lancerLaPartieDepuisLobby() {
  el("lobby-error").textContent = "";

  // Garde-fou client (le serveur refuserait StartRound de toute façon, voir GameHub.StartRound) —
  // évite une HubException confuse si ce déclenchement vient de l'écran public (bouton toujours
  // visible là-bas, contrairement à btn-start-round qui est nativement désactivé ici).
  if (!state.partieConfiguree) {
    el("lobby-error").textContent = "Configure le blindtest avant de démarrer la partie.";
    return;
  }

  afficherIntroSerie(); // toujours la première série de la partie à cet instant
}

el("btn-start-round").addEventListener("click", lancerLaPartieDepuisLobby);
displayBridge.initDisplayBridge(state, { onStartRoundRequested: lancerLaPartieDepuisLobby });

// ----- Override manuel (réponses tapées ambiguës) -----
// Délégation d'événement plutôt qu'un listener par bouton : renderResults (render.js) reconstruit
// #results-body à chaque rendu et se contente de poser des data-attributes, sans dépendre de
// transport.js (render.js ne doit jamais appeler le réseau — voir docs/refactor-decisions.md section 2).

el("results-body").addEventListener("click", (e) => {
  const btn = e.target.closest(".btn-override");
  if (!btn) return;
  validerManuellement(btn.dataset.playerId, btn.dataset.estCorrecte === "true");
});

async function validerManuellement(playerId, estCorrecte) {
  let resultat;
  try {
    resultat = await transport.invoke.validateAnswerManually(playerId, estCorrecte);
  } catch (err) {
    console.error(err);
    el("round-ended-note").textContent = "Erreur override : " + (err.message || err);
    return;
  }

  const entree = state.dernierResultats.find((r) => r.playerId === playerId);
  if (!entree) return;
  entree.estCorrecte = resultat.estCorrecte;
  entree.points = resultat.points;

  if (state.currentRevealInfo.resultats) {
    const entreePublique = state.currentRevealInfo.resultats.find((r) => r.playerId === playerId);
    if (entreePublique) entreePublique.estCorrecte = resultat.estCorrecte;
  }
  notify();
}

// ----- Actions du panneau de contrôle -----

el("btn-pause").addEventListener("click", async () => {
  try {
    await transport.invoke.pauseGame();
  } catch (err) {
    console.error(err);
    el("round-error").textContent = "Erreur : " + (err.message || err);
  }
});

el("btn-resume").addEventListener("click", async () => {
  try {
    await transport.invoke.resumeGame();
  } catch (err) {
    console.error(err);
    el("round-error").textContent = "Erreur : " + (err.message || err);
  }
});

el("btn-leaderboard").addEventListener("click", async () => {
  timers.annulerMinuteur("auto-next");
  timers.annulerMinuteur("auto-fin-ou-serie-suivante");
  try {
    await transport.invoke.showLeaderboard();
  } catch (err) {
    console.error(err);
    el("round-error").textContent = "Erreur : " + (err.message || err);
  }
});

el("btn-close-leaderboard").addEventListener("click", () => {
  state.leaderboardOpen = false;
  displayBridge.sendLeaderboardHide();
  notify();
});

el("btn-next-round").addEventListener("click", () => {
  timers.annulerMinuteur("auto-next");
  avancerRoundSuivant();
});

el("btn-end-game").addEventListener("click", async () => {
  timers.annulerMinuteur("auto-next");
  try {
    await transport.invoke.endGame();
  } catch (err) {
    console.error(err);
    el("round-ended-note").textContent = "Erreur : " + (err.message || err);
  }
});

// Series-aware : "Série suivante maintenant" (séries restantes) ou "Terminer maintenant" (dernière
// série) — le libellé est mis à jour par render.js:renderBonusResult, cohérent avec le minuteur
// "auto-fin-ou-serie-suivante" ci-dessus (ce bouton ne fait que sauter son délai, jamais l'inverse).
el("btn-end-now").addEventListener("click", async () => {
  timers.annulerMinuteur("auto-fin-ou-serie-suivante");
  if (state.serieCouranteIndex < state.nombreSeriesTotal) {
    await avancerSerieSuivante();
    return;
  }
  try {
    await transport.invoke.endGame();
  } catch (err) {
    console.error(err);
    el("bonus-end-note").textContent = "Erreur : " + (err.message || err);
  }
});

el("btn-rejouer").addEventListener("click", async () => {
  el("ended-error").textContent = "";
  try {
    await transport.invoke.rejouerPartie();
  } catch (err) {
    console.error(err);
    el("ended-error").textContent = "Erreur : " + (err.message || err);
  }
});

// Retour utilisateur (2026-09-27) — pendant de "Rejouer" : ferme ce salon côté serveur (les joueurs
// reviennent à l'écran « rejoindre », voir GameHub.FermerSalon) et repart sur l'écran de création
// pour obtenir un nouveau code, typiquement quand une partie du groupe arrête de jouer.
// Accessible depuis l'écran de fin et depuis le menu « ⋯ » (refonte UI, lot 2), donc à tout moment.
async function nouveauSalon() {
  if (!state.gameCode) return;
  if (!window.confirm("Fermer ce salon ? Les joueurs devront rejoindre le nouveau code.")) return;
  try {
    await transport.invoke.fermerSalon();
  } catch (err) {
    console.error(err);
    erreurEcranCourant("Erreur : " + (err.message || err));
    return;
  }

  timers.annulerTousLesMinuteurs();
  timers.stopTimer();
  audio.pauseAudioEnDouceur();
  clearHostSession();
  state.gameCode = null;
  state.hostSecret = null;
  state.players = [];
  state.equipes = [];
  state.equipeParJoueur = {};
  state.partieConfiguree = false;
  state.jeuEnPause = false;
  state.leaderboardOpen = false;
  state.currentScreen = null; // écran de création piloté par config.js, voir render.js
  state.currentDisplayScreen = "idle";
  state.currentScoresInfo = null;
  state.currentTitresInfo = [];
  state.titreIndexAffiche = -1;
  state.morceauxJoues = [];
  notify();
  showScreen("screen-setup");
}

el("btn-nouveau-salon").addEventListener("click", nouveauSalon);

// Affiche un message dans la zone d'erreur de l'écran visible — pour les actions de la régie
// (menu, colonne d'actions) qui ne sont rattachées à aucun écran en particulier.
function erreurEcranCourant(message) {
  const ecran = [...document.querySelectorAll("section.screen")].find((s) => !s.classList.contains("hidden"));
  const zone = ecran?.querySelector(".error");
  if (zone) zone.textContent = message;
  else console.error(message);
}

// ----- Régie (refonte UI, lot 2) -----

// Action principale de la colonne : relaie le clic au bouton d'origine de l'écran courant (masqué
// en mode régie), avec ses gardes et ses messages d'erreur.
el("btn-action-principale").addEventListener("click", () => {
  const id = ACTION_SUIVANTE_PAR_ECRAN[state.currentScreen];
  if (id) cliquerSiDisponible(id);
});

el("btn-rail-terminer").addEventListener("click", async () => {
  if (!window.confirm("Terminer la partie maintenant ?")) return;
  timers.annulerMinuteur("auto-next");
  timers.annulerMinuteur("auto-fin-ou-serie-suivante");
  try {
    await transport.invoke.endGame();
  } catch (err) {
    console.error(err);
    erreurEcranCourant("Erreur : " + (err.message || err));
  }
});

// Menu « ⋯ » : actions rares, sorties de la vue principale.
function fermerMenu() {
  el("menu-panel").classList.add("hidden");
  el("btn-menu").setAttribute("aria-expanded", "false");
}

el("btn-menu").addEventListener("click", (event) => {
  event.stopPropagation();
  const ouvrir = el("menu-panel").classList.contains("hidden");
  el("menu-nouveau-salon").disabled = !state.gameCode;
  el("menu-panel").classList.toggle("hidden", !ouvrir);
  el("btn-menu").setAttribute("aria-expanded", String(ouvrir));
});
el("menu-panel").addEventListener("click", fermerMenu);
document.addEventListener("click", (event) => {
  if (!event.target.closest?.(".menu")) fermerMenu();
});
document.addEventListener("keydown", (event) => {
  if (event.key === "Escape") fermerMenu();
});
el("menu-nouveau-salon").addEventListener("click", nouveauSalon);

// Avertissement « panneau réservé à l'organisateur » : affiché au premier lancement seulement.
const NOTE_VUE_STORAGE_KEY = "blindify_note_organisateur_vue";
function afficherNoteUneFois() {
  let dejaVue = false;
  try {
    dejaVue = localStorage.getItem(NOTE_VUE_STORAGE_KEY) === "1";
  } catch {
    // Stockage indisponible : on affiche la note, sans pouvoir s'en souvenir.
  }
  el("control-note").classList.toggle("hidden", dejaVue);
}
el("btn-note-compris").addEventListener("click", () => {
  el("control-note").classList.add("hidden");
  try {
    localStorage.setItem(NOTE_VUE_STORAGE_KEY, "1");
  } catch {
    // Ignoré : la note réapparaîtra au prochain lancement.
  }
});
afficherNoteUneFois();

el("btn-open-display").addEventListener("click", () => displayBridge.openDisplayWindow());

el("btn-reecouter").addEventListener("click", () => audio.reecouterDepuisDebut());

el("btn-reveler").addEventListener("click", async () => {
  el("round-error").textContent = "";
  try {
    await transport.invoke.revelerMaintenant();
  } catch (err) {
    console.error(err);
    el("round-error").textContent = "Erreur : " + (err.message || err);
  }
});

// ----- Raccourcis clavier (refonte UI, lot 1) -----
// Espace : action suivante de l'écran courant · P : pause/reprise · L : tableau général ·
// R : réécouter. Rappelés à côté de chaque bouton (attribut data-kbd, voir style.css). Déclenchent
// le bouton correspondant plutôt qu'une logique dupliquée : mêmes gardes (bouton masqué ou
// désactivé = rien ne se passe), mêmes messages d'erreur.
function cliquerSiDisponible(id) {
  const bouton = el(id);
  if (!bouton || bouton.disabled || bouton.closest(".hidden")) return false;
  bouton.click();
  return true;
}

function estChampDeSaisie(cible) {
  return cible instanceof HTMLElement && (cible.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(cible.tagName));
}

document.addEventListener("keydown", (event) => {
  if (event.ctrlKey || event.metaKey || event.altKey || event.repeat) return;
  if (estChampDeSaisie(event.target)) return;
  // Aucun raccourci tant que l'overlay de redémarrage du serveur est ouvert (saisie du mot de passe).
  if (!el("restart-overlay")?.classList.contains("hidden")) return;

  if (event.code === "Space") {
    // Toujours bloqué, même sans action : sinon Espace « clique » aussi le bouton qui a le focus
    // (le dernier cliqué à la souris), en plus de l'action du raccourci.
    event.preventDefault();
    const id = ACTION_SUIVANTE_PAR_ECRAN[state.currentScreen];
    if (id) cliquerSiDisponible(id);
    return;
  }

  switch (event.key.toLowerCase()) {
    case "p":
      if (!cliquerSiDisponible("btn-pause")) cliquerSiDisponible("btn-resume");
      break;
    case "l":
      cliquerSiDisponible("btn-leaderboard");
      break;
    case "r":
      cliquerSiDisponible("btn-reecouter");
      break;
  }
});

// Le navigateur active un bouton focalisé au relâchement d'Espace : bloqué aussi, pour la même
// raison que ci-dessus.
document.addEventListener("keyup", (event) => {
  if (event.code === "Space" && !estChampDeSaisie(event.target)) event.preventDefault();
});

// ----- Signalement en direct (V2, section 12.4) -----
// Délégation d'événement : les lignes de #flags-list sont générées dynamiquement (render.js), on ne
// peut pas leur attacher un listener individuel au chargement de la page.
el("flags-list").addEventListener("click", async (event) => {
  const bouton = event.target.closest(".flags-submit");
  if (!bouton) return;

  const ligne = bouton.closest("[data-track-id]");
  const trackId = ligne.dataset.trackId;
  const raison = ligne.querySelector(".flags-raison").value;
  const commentaire = ligne.querySelector(".flags-commentaire").value.trim();
  const statusEl = ligne.querySelector(".flags-status");

  bouton.disabled = true;
  statusEl.textContent = "";
  statusEl.classList.remove("flags-status--error");
  try {
    const resultat = await transport.invoke.signalerMorceau(trackId, raison, commentaire);
    statusEl.textContent = resultat.dejaSignale ? "Déjà signalé." : "Signalé.";
  } catch (err) {
    console.error(err);
    statusEl.textContent = "Erreur : " + (err.message || err);
    statusEl.classList.add("flags-status--error");
  } finally {
    bouton.disabled = false;
  }
});

// ----- Redémarrage du serveur (retour utilisateur) -----
// Requête HTTP directe (pas SignalR) vers POST /api/admin/restart — indépendante de la connexion
// hub, utilisable même si la partie n'a jamais démarré. Le serveur exige un mot de passe
// (Admin:RestartPassword) et répond 503 si la fonctionnalité n'est pas configurée côté backend.

el("btn-open-restart").addEventListener("click", () => {
  el("restart-password").value = "";
  el("restart-error").textContent = "";
  el("restart-note").textContent = "";
  el("btn-confirm-restart").textContent = "Redémarrer maintenant";
  el("restart-overlay").classList.remove("hidden");
  el("restart-password").focus();
});

el("btn-cancel-restart").addEventListener("click", () => {
  el("restart-overlay").classList.add("hidden");
});

el("btn-confirm-restart").addEventListener("click", async () => {
  const errorEl = el("restart-error");
  const noteEl = el("restart-note");
  const btn = el("btn-confirm-restart");
  errorEl.textContent = "";
  noteEl.textContent = "";
  btn.disabled = true;

  try {
    const reponse = await fetch(`${state.serverBaseUrl}/api/admin/restart`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ password: el("restart-password").value }),
    });

    if (reponse.status === 401) {
      errorEl.textContent = "Mot de passe incorrect.";
    } else if (reponse.status === 503) {
      errorEl.textContent = "Redémarrage désactivé côté serveur (Admin:RestartPassword non configuré).";
    } else if (!reponse.ok) {
      errorEl.textContent = `Erreur inattendue (${reponse.status}).`;
    } else {
      // Le serveur va couper la connexion sous peu (voir Program.cs) — withAutomaticReconnect()
      // reprendra tout seul une fois le conteneur relancé par Docker, mais la partie en cours
      // (état 100% en mémoire côté serveur) est perdue : pas de tentative de resynchronisation ici.
      noteEl.textContent = "Redémarrage en cours — reconnexion automatique dans quelques secondes.";
      btn.textContent = "Redémarrage en cours...";
      setTimeout(() => el("restart-overlay").classList.add("hidden"), 2500);
    }
  } catch (err) {
    console.error(err);
    errorEl.textContent = "Erreur réseau : " + (err.message || err);
  } finally {
    btn.disabled = false;
  }
});

// ----- Démarrage : connexion automatique à l'adresse depuis laquelle la page a été chargée -----
// host/ est servi par le backend lui-même (voir docs/architecture.md "Dockerisation"), donc
// l'adresse du serveur est toujours celle de la barre d'adresse du navigateur, pas la peine de la
// faire retaper. Repli sur la dernière adresse connue (localStorage) si l'origine n'est pas
// exploitable (page ouverte en fichier local, ex. file://) — puis sur la saisie manuelle en cas
// d'échec/timeout, même logique de tentative silencieuse et bornée que côté Flutter
// (GameConnection.init).
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

// ----- Télécommande (refonte UI, lot 3) -----
// Publie au serveur, à chaque changement, ce que ferait « Action suivante » (libellé + disponibilité,
// lus sur le bouton de la colonne d'actions, voir render.js:renderRegie) — relayé aux admins pour leur
// télécommande (GameHub.PublierEtatRegie). Jamais d'information sur le morceau.
let dernierEtatRegie = "";
function publierEtatRegieSiChange() {
  if (!state.gameCode || !state.currentScreen) return;
  const bouton = el("btn-action-principale");
  const etat = { libelleAction: bouton.textContent.trim() || "—", actionDisponible: !bouton.disabled };
  const cle = JSON.stringify(etat);
  if (cle === dernierEtatRegie) return;
  dernierEtatRegie = cle;
  transport.invoke.publierEtatRegie(etat).catch((err) => {
    dernierEtatRegie = ""; // retenté au prochain changement d'état
    console.error("PublierEtatRegie :", err);
  });
}
