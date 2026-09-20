"use strict";

// Bootstrap et câblage — orchestre tous les autres modules (docs/refactor-decisions.md section 2).
// Point d'entrée unique chargé par host/index.html (`<script type="module" src="main.js">`).

import { state, notify, subscribe, loadHostSession, clearHostSession } from "./state.js";
import * as transport from "./transport.js";
import { registerHandlers, mutations } from "./handlers.js";
import { render, setConnected, showScreen } from "./render.js";
import * as audio from "./audio.js";
import * as timers from "./timers.js";
import * as displayBridge from "./display-bridge.js";
import { chargerTagsDisponibles } from "./config.js";

const el = (id) => document.getElementById(id);

// Écran host censé rester allumé toute la soirée (contrôle du jeu + lecture audio) — best-effort,
// support Screen Wake Lock API pas garanti sur tous les navigateurs desktop (voir docs/architecture.md
// "Écran toujours allumé"). Jamais bloquant si l'API est absente ou refuse (onglet en arrière-plan).
if ("wakeLock" in navigator) {
  navigator.wakeLock.request("screen").catch(() => {});
}

subscribe(render);
subscribe(displayBridge.syncDisplay);

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

  const connection = transport.creerConnexion(url);
  registerHandlers(connection, { onConnectionChanged: setConnected, onEvent: handleEvent });

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

el("btn-connect").addEventListener("click", () => tenterConnexion(el("server-url").value));

// V2 — reconnexion host : restaure le contrôle d'une partie déjà créée après un refresh accidentel
// de la page (code + hostSecret en sessionStorage, voir state.js). Le snapshot renvoyé par
// RejoinAsHost (position audio, pause, mode) ne porte pas assez d'info (joueurs, série, options QCM)
// pour reconstruire fidèlement l'écran de round : on atterrit sur le lobby avec le contrôle restauré
// (pause/reprise/tableau général/fin de partie redeviennent utilisables) plutôt que d'afficher un
// écran de round forcément incomplet. Retourne true si une session a été reprise avec succès.
async function tenterResumeHostSession() {
  const session = loadHostSession();
  if (!session) return false;

  try {
    const snapshot = await transport.invoke.rejoinAsHost(session.code, session.hostSecret);
    state.gameCode = session.code;
    state.hostSecret = session.hostSecret;
    state.jeuEnPause = snapshot.enPause;

    if (snapshot.trackId) {
      audio.resumeAudio(state.serverBaseUrl, snapshot.filePath, snapshot.positionAudioMs, snapshot.enPause);
    }

    state.currentScreen = "lobby";
    showScreen("screen-lobby");
    el("lobby-error").textContent =
      "Partie en cours reprise après rechargement de la page — utilise les actions ci-dessous (pause, tableau général, fin de partie) ; l'écran de round n'est pas reconstruit.";
    notify();
    return true;
  } catch (err) {
    // Secret invalide ou partie disparue (backend redémarré entre-temps, état 100% en mémoire) —
    // pas la peine de réessayer indéfiniment, ni de bloquer la connexion pour autant.
    console.error("RejoinAsHost:", err);
    clearHostSession();
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
      timers.minuteurAnnulable("auto-intro-serie", DUREE_INTRO_SERIE_MS, demarrerRoundApresIntro, {
        onTick: (restantMs) => {
          el("serie-intro-note").textContent = `Démarrage automatique dans ${Math.ceil(restantMs / 1000)}s...`;
        },
        onCancel: () => { el("serie-intro-note").textContent = ""; },
      });
      break;

    case "RoundStarted":
      audio.playAudio(state.serverBaseUrl, payload.filePath); // toujours depuis le début — le refrain n'est joué qu'au reveal
      timers.startTimer(payload.dureeFenetreReponseMs, el("timer-fill"));
      displayBridge.resetPlayerAnswered();
      break;

    case "PlayerAnswered":
      // Réaction visuelle + classement de rapidité sur l'écran public uniquement (retour
      // utilisateur) — jamais sur le téléphone des joueurs, jamais l'exactitude de la réponse
      // (voir PlayerAnsweredDto côté backend).
      displayBridge.sendPlayerAnswered(payload);
      break;

    case "RoundEnded":
      timers.stopTimer();
      displayBridge.resetPlayerAnswered();
      // Au reveal (tout le monde a répondu) : on saute au refrain si on en connaît un pour ce
      // morceau, sinon on retombe sur le comportement "musique continue" habituel.
      if (state.refrainCourantMs !== null) {
        audio.jouerRefrain(state.refrainCourantMs);
      } else if (!el("setup-audio-continu").checked) {
        audio.pauseAudioEnDouceur();
      }
      timers.minuteurAnnulable("auto-next", delaiEnchainementMs(), avancerRoundSuivant, {
        onTick: (restantMs) => {
          const label = state.roundsDemarres >= state.nombreRoundsParSerie ? "Question bonus" : "Round suivant";
          el("auto-next-note").textContent = `${label} dans ${Math.ceil(restantMs / 1000)}s...`;
        },
        onCancel: () => { el("auto-next-note").textContent = ""; },
      });
      break;

    case "GamePaused":
      audio.pauseAudioEnDouceur();
      timers.pauseTimer();
      timers.annulerMinuteur("auto-next");
      timers.annulerMinuteur("auto-fin-ou-serie-suivante");
      break;

    case "GameResumed":
      audio.lancerLecture();
      audio.fadeAudioVolume(1, 450);
      timers.resumeTimer();
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

    case "BonusQuestionStarted": {
      displayBridge.resetPlayerAnswered();
      const demarrerAudioEtMinuteur = () => {
        const rate = payload.ralentissementActive ? payload.facteurRalentissement : 1;
        audio.playAudio(state.serverBaseUrl, payload.filePath, rate); // depuis le début — c'est la devinette elle-même
        timers.startTimer(payload.dureePhaseQuestionMs, el("bonus-question-timer-fill"));
      };
      // Retour utilisateur : en mode course, l'audio démarrait ici immédiatement alors que l'app
      // joueur force un écran d'intro "Mode course" pendant DUREE_INTRO_COURSE_MS avant de laisser
      // répondre (voir app/lib/services/game_connection.dart:_dureeIntroCourse) — ~2,5s de musique
      // audibles sur l'enceinte du host sans qu'aucun joueur ne puisse encore réagir. Les deux
      // délais doivent rester identiques ; DemarrerAudioEtMinuteur est annulable via
      // annulerMinuteur("bonus-course-intro") si un BonusResult arrive avant l'échéance.
      if (payload.estCourse) {
        timers.minuteurAnnulable("bonus-course-intro", DUREE_INTRO_COURSE_MS, demarrerAudioEtMinuteur);
      } else {
        demarrerAudioEtMinuteur();
      }
      break;
    }

    case "BonusResult":
      timers.annulerMinuteur("bonus-course-intro");
      timers.stopTimer();
      displayBridge.resetPlayerAnswered();
      audio.remettreVitesseNormale(); // remis à la vitesse normale pour la suite (fin de partie, replay...)
      if (state.refrainCourantMs !== null) {
        audio.jouerRefrain(state.refrainCourantMs);
      } else {
        audio.pauseAudioEnDouceur();
      }
      if (state.serieCouranteIndex < state.nombreSeriesTotal) {
        timers.minuteurAnnulable("auto-fin-ou-serie-suivante", delaiEnchainementMs(), avancerSerieSuivante, {
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
      break;

    case "LeaderboardShown":
      displayBridge.sendLeaderboardShow(payload);
      break;
  }

  notify();
}

function delaiEnchainementMs() {
  return Math.max(1, parseInt(el("setup-delai-enchainement").value, 10) || 10) * 1000;
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
    await transport.invoke.nextRound();
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

el("btn-open-display").addEventListener("click", () => displayBridge.openDisplayWindow());

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
