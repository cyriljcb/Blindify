"use strict";

// Lecture audio du panneau de contrôle — seul module autorisé à toucher <audio> (voir CLAUDE.md :
// l'audio ne sort jamais vers les joueurs, seul le host le joue). Jamais piloté par render.js — la
// balise reste hors du flux de rendu réactif (docs/refactor-decisions.md section 2).

const el = (id) => document.getElementById(id);
const audioEl = el("player-audio");
const manualPlayBtn = el("btn-manual-play");

let fadeIntervalId = null;

// Refonte UI (lot 2) — volume du lecteur maison : le volume effectif est le produit du niveau de
// fondu (0 → 1, piloté par les transitions ci-dessous) et du volume principal choisi par le host
// (curseur « Vol. »), pour que les fondus ne l'écrasent jamais.
const VOLUME_STORAGE_KEY = "blindify_volume_host";
let niveauFondu = 1;
let volumeMaitre = lireVolumeMaitre();

function lireVolumeMaitre() {
  try {
    const v = parseFloat(localStorage.getItem(VOLUME_STORAGE_KEY));
    return Number.isFinite(v) ? Math.min(1, Math.max(0, v)) : 1;
  } catch {
    return 1;
  }
}

function definirNiveauFondu(niveau) {
  niveauFondu = Math.min(1, Math.max(0, niveau));
  audioEl.volume = niveauFondu * volumeMaitre;
}

// Fondu de volume — évite les coupures sèches entre la découverte et le reveal, ou entre
// deux morceaux qui s'enchaînent automatiquement.
export function fadeAudioVolume(cible, dureeMs, onDone) {
  clearInterval(fadeIntervalId);
  const depart = niveauFondu;
  const debutTs = performance.now();
  fadeIntervalId = setInterval(() => {
    const t = Math.min(1, (performance.now() - debutTs) / dureeMs);
    definirNiveauFondu(depart + (cible - depart) * t);
    if (t >= 1) {
      clearInterval(fadeIntervalId);
      fadeIntervalId = null;
      if (onDone) onDone();
    }
  }, 30);
}

export function lancerLecture() {
  const playPromise = audioEl.play();
  if (playPromise && typeof playPromise.catch === "function") {
    playPromise.catch(() => manualPlayBtn.classList.remove("hidden"));
  }
}

export function playAudio(serverBaseUrl, filePath, playbackRate = 1) {
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
    definirNiveauFondu(0);
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
export function jouerRefrain(refrainStartMs) {
  fadeAudioVolume(0, 280, () => {
    audioEl.currentTime = refrainStartMs / 1000;
    lancerLecture();
    fadeAudioVolume(1, 450);
  });
}

// Reprise après RejoinAsHost (V2) — contrairement à playAudio, reprend à une position donnée plutôt
// que depuis le début. currentTime n'est fiable qu'une fois les métadonnées chargées (durée connue),
// d'où l'attente de "loadedmetadata" plutôt qu'une affectation immédiate après load().
export function resumeAudio(serverBaseUrl, filePath, positionMs, enPause) {
  audioEl.src = `${serverBaseUrl}/files/${filePath}`;
  audioEl.load();
  manualPlayBtn.classList.add("hidden");

  const reprendre = () => {
    audioEl.currentTime = positionMs / 1000;
    if (enPause) {
      audioEl.pause();
      definirNiveauFondu(1);
    } else {
      definirNiveauFondu(0);
      lancerLecture();
      fadeAudioVolume(1, 450);
    }
  };

  audioEl.addEventListener("loadedmetadata", reprendre, { once: true });
}

export function pauseAudioEnDouceur() {
  fadeAudioVolume(0, 320, () => audioEl.pause());
}

export function remettreVitesseNormale() {
  audioEl.playbackRate = 1;
}

manualPlayBtn.addEventListener("click", () => {
  audioEl.play();
  manualPlayBtn.classList.add("hidden");
});

// Refonte UI (lot 1) — raccourci R / bouton « Réécouter depuis le début » du panneau host : relance
// le morceau courant au début sans recharger la source (vitesse de la question bonus conservée).
export function reecouterDepuisDebut() {
  if (!audioEl.src) return;
  fadeAudioVolume(0, 200, () => {
    audioEl.currentTime = 0;
    lancerLecture();
    fadeAudioVolume(1, 350);
  });
}

// ----- Lecteur maison (refonte UI, lot 2) -----
// Remplace les contrôles natifs du navigateur : lecture/pause de la musique seule (sans mettre la
// partie en pause), progression cliquable avec le repère du refrain, volume principal. Jamais le
// titre du morceau : le host joue souvent.

const playBtn = el("btn-lecteur-play");
const progressionEl = el("lecteur-progression");
const rempliEl = el("lecteur-rempli");
const refrainEl = el("lecteur-refrain");
const tempsEl = el("lecteur-temps");
const volumeEl = el("lecteur-volume");
let refrainMs = null;

function formaterTemps(secondes) {
  if (!Number.isFinite(secondes) || secondes < 0) return "0:00";
  const s = Math.floor(secondes);
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;
}

function majLecteur() {
  const duree = audioEl.duration;
  const ratio = Number.isFinite(duree) && duree > 0 ? audioEl.currentTime / duree : 0;
  rempliEl.style.width = `${Math.min(100, ratio * 100)}%`;
  tempsEl.textContent = `${formaterTemps(audioEl.currentTime)} / ${formaterTemps(duree)}`;
  playBtn.textContent = audioEl.paused ? "▶" : "❚❚";
  const refrainVisible = refrainMs !== null && Number.isFinite(duree) && duree > 0;
  refrainEl.classList.toggle("hidden", !refrainVisible);
  if (refrainVisible) refrainEl.style.left = `${Math.min(100, (refrainMs / 1000 / duree) * 100)}%`;
}

// Appelé par main.js à chaque nouveau morceau (round ou question bonus) — null si le refrain n'est
// pas connu pour ce morceau.
export function definirRepereRefrain(ms) {
  refrainMs = ms ?? null;
  majLecteur();
}

playBtn.addEventListener("click", () => {
  if (!audioEl.src) return;
  if (audioEl.paused) {
    lancerLecture();
    fadeAudioVolume(1, 250);
  } else {
    pauseAudioEnDouceur();
  }
});

progressionEl.addEventListener("click", (event) => {
  const duree = audioEl.duration;
  if (!Number.isFinite(duree) || duree <= 0) return;
  const rect = progressionEl.getBoundingClientRect();
  audioEl.currentTime = Math.min(1, Math.max(0, (event.clientX - rect.left) / rect.width)) * duree;
});

volumeEl.value = String(volumeMaitre);
volumeEl.addEventListener("input", () => {
  volumeMaitre = parseFloat(volumeEl.value);
  definirNiveauFondu(niveauFondu);
  try {
    localStorage.setItem(VOLUME_STORAGE_KEY, String(volumeMaitre));
  } catch {
    // Ignoré : le volume revient à 100 % au prochain lancement.
  }
});

for (const evenement of ["timeupdate", "loadedmetadata", "play", "pause", "ended", "emptied"]) {
  audioEl.addEventListener(evenement, majLecteur);
}
majLecteur();
