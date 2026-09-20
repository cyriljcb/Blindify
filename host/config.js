"use strict";

// Formulaires de création/configuration de partie (écrans "connect"/"setup" + bloc
// #blindtest-config du lobby) — restent pilotés par des event listeners DOM directs, hors du flux
// état→rendu (docs/refactor-decisions.md section 2 : ni données serveur à refléter en continu, ni
// input à protéger d'un re-render pendant la saisie).
//
// Depuis docs/refactor-decisions.md section 1, la répartition des thèmes par série, les paliers de
// mise bonus et le tirage des modes de round sont calculés côté serveur (Blindify.Application.
// Rounds.SeriesPlanner) — ce module se contente de collecter les choix de l'écran et d'envoyer une
// intention (nombre de séries/rounds/durée + vivier de thèmes), plus léger qu'avant.

import { state, notify, saveHostSession } from "./state.js";
import { invoke } from "./transport.js";
import { escapeHtml } from "./shared/format.js";

const el = (id) => document.getElementById(id);

export async function chargerTagsDisponibles() {
  try {
    const response = await fetch(`${state.serverBaseUrl}/api/tags`);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    state.tagsDisponibles = await response.json();
  } catch (err) {
    console.error("Impossible de charger les thèmes disponibles :", err);
    state.tagsDisponibles = [];
  }
  renderTagPicker();
}

function renderTagPicker() {
  const container = el("setup-tags-container");
  // La case "aléatoire" est statique dans le HTML — on ne retire que les tuiles générées.
  container.querySelectorAll(".tag-tile:not(.tag-tile--random)").forEach((tile) => tile.remove());

  for (const tag of state.tagsDisponibles) {
    const label = document.createElement("label");
    label.className = "tag-tile";
    label.innerHTML = `<input type="checkbox" value="${escapeHtml(tag)}" /> ${escapeHtml(tag)}`;
    container.appendChild(label);
  }
}

// Cocher "aléatoire" grise et décoche tous les autres thèmes (le payload envoie alors
// themesVivier: [], qui pioche dans tout le catalogue côté backend).
el("setup-tags-aleatoire").addEventListener("change", (e) => {
  const aleatoire = e.target.checked;
  el("setup-tags-container")
    .querySelectorAll(".tag-tile:not(.tag-tile--random) input")
    .forEach((input) => {
      input.disabled = aleatoire;
      if (aleatoire) input.checked = false;
    });
});

// Case "aléatoire" cochée -> tags: [] (tout le catalogue). Sinon, un thème coché ou plus (le vivier
// candidat — voir SeriesPlanner.AssignerThemesAuxSeries côté serveur pour la répartition par série).
function themesSelectionnes() {
  if (el("setup-tags-aleatoire").checked) return [];
  return Array.from(el("setup-tags-container").querySelectorAll(".tag-tile:not(.tag-tile--random) input:checked")).map(
    (input) => input.value
  );
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

// Intention envoyée au serveur (voir en-tête de fichier) — les valeurs par défaut non exposées dans
// cet écran (points, pénalités, durées des phases bonus) restent celles de
// ConfigurerPartieRequestDto côté backend, jamais recalculées ici.
function buildConfigurerPartieRequest() {
  return {
    nombreSeries: Math.max(1, parseInt(el("setup-nombre-series").value, 10) || 1),
    nombreRoundsClassiques: parseInt(el("setup-nombre-rounds").value, 10),
    dureeFenetreReponseMs: parseInt(el("setup-duree-fenetre").value, 10) * 1000,
    themesVivier: themesSelectionnes(),
    config: null,
  };
}

el("setup-mode-equipe").addEventListener("change", (e) => {
  el("setup-equipes-wrapper").classList.toggle("hidden", !e.target.checked);
});

el("btn-create-game").addEventListener("click", async () => {
  const errorEl = el("setup-error");
  errorEl.textContent = "";

  try {
    const payload = buildCreateGameRequest();
    const result = await invoke.createGame(payload);
    state.gameCode = result.code;
    state.hostSecret = result.hostSecret;
    saveHostSession();
    state.partieConfiguree = false;
    el("configurer-error").textContent = "";
    el("configurer-note").textContent = "";
    state.roundsDemarres = 0;
    state.scoreHistory = [];
    state.dernierScoreDto = null;
    state.historiqueLabelEnAttente = null;
    state.players = [];
    state.equipes = result.teams ?? [];
    state.equipeParJoueur = {};
    state.currentScreen = "lobby";
    state.currentDisplayScreen = "lobby";
    state.currentRoundInfo = {};
    state.currentRevealInfo = {};
    state.currentScoresInfo = null;
    state.currentTitresInfo = [];
    state.titreIndexAffiche = -1;
    state.morceauxJoues = [];
    notify();
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
    await invoke.configurerPartie(payload);
    state.nombreRoundsParSerie = payload.nombreRoundsClassiques;
    state.nombreSeriesTotal = payload.nombreSeries;
    // Les thèmes réellement assignés à chaque série ne sont plus connus ici (calculés côté serveur,
    // voir SeriesPlanner) — appris série par série via l'événement SerieAnnoncee, voir handlers.js.
    state.tagsParSerieCourante = [];
    state.serieCouranteIndex = 0;
    state.partieConfiguree = true;
    notify();
    el("configurer-note").textContent = "Configuration validée — prêt à démarrer.";
  } catch (err) {
    console.error(err);
    errorEl.textContent = "Erreur : " + (err.message || err);
  }
});

