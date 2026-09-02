"use strict";

// Lecture audio du panneau de contrôle — seul module autorisé à toucher <audio> (voir CLAUDE.md :
// l'audio ne sort jamais vers les joueurs, seul le host le joue). Jamais piloté par render.js — la
// balise reste hors du flux de rendu réactif (docs/refactor-decisions.md section 2).

const el = (id) => document.getElementById(id);
const audioEl = el("player-audio");
const manualPlayBtn = el("btn-manual-play");

let fadeIntervalId = null;

// Fondu de volume — évite les coupures sèches entre la découverte et le reveal, ou entre
// deux morceaux qui s'enchaînent automatiquement.
export function fadeAudioVolume(cible, dureeMs, onDone) {
  clearInterval(fadeIntervalId);
  const depart = audioEl.volume;
  const debutTs = performance.now();
  fadeIntervalId = setInterval(() => {
    const t = Math.min(1, (performance.now() - debutTs) / dureeMs);
    audioEl.volume = depart + (cible - depart) * t;
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
    audioEl.volume = 0;
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
