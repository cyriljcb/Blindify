"use strict";

// Minuteurs du panneau de contrôle — voir docs/refactor-decisions.md section 3. Deux familles
// distinctes cohabitent ici :
// 1. `minuteurAnnulable` : primitive unique d'enchaînement automatique annulable, remplace les 4
//    paires jumelles scheduleAutoNext/scheduleAutoEndGame/scheduleAutoNextSerie/scheduleAutoStartSerie
//    + leurs cancelX respectifs — un registre à clés plutôt que 4 couples de variables setTimeout/
//    setInterval à annuler individuellement dans le bon ordre.
// 2. Le minuteur visuel (startTimer/pauseTimer/resumeTimer/stopTimer) — approximatif, le serveur
//    reste seul juge du timing. Garde un accès DOM direct à l'élément timer-fill ciblé (comme
//    audio.js pour <audio>) : un re-render complet à 100ms via l'état global serait un gâchis que
//    la réactivité de render.js n'a pas besoin de résoudre.

const registreMinuteurs = new Map(); // clé -> { timeoutId, intervalId }

// onCancel : appelé aussi bien sur annulation explicite qu'à l'échéance naturelle (juste avant
// `action`) — typiquement pour effacer le texte de décompte ("Round suivant dans 3s..."), qui ne
// doit pas non plus persister une fois l'enchaînement effectivement déclenché.
export function minuteurAnnulable(cle, delaiMs, action, { onTick, onCancel } = {}) {
  annulerMinuteur(cle);
  const finA = Date.now() + delaiMs;
  const entry = { onCancel };

  if (onTick) {
    onTick(delaiMs);
    entry.intervalId = setInterval(() => onTick(Math.max(0, finA - Date.now())), 250);
  }

  entry.timeoutId = setTimeout(() => {
    annulerMinuteur(cle);
    action();
  }, delaiMs);

  registreMinuteurs.set(cle, entry);
}

export function annulerMinuteur(cle) {
  const entry = registreMinuteurs.get(cle);
  if (!entry) return;
  if (entry.timeoutId) clearTimeout(entry.timeoutId);
  if (entry.intervalId) clearInterval(entry.intervalId);
  registreMinuteurs.delete(cle);
  entry.onCancel?.();
}

export function annulerTousLesMinuteurs() {
  for (const cle of [...registreMinuteurs.keys()]) annulerMinuteur(cle);
}

// ----- Minuteur visuel -----

let timerInterval = null;
let timerEndAt = null;
let timerDurationMs = 0;
let timerPausedRemainingMs = null;
let timerFillEl = null;

function updateTimer() {
  if (timerEndAt === null || !timerFillEl) return;
  const remaining = Math.max(0, timerEndAt - Date.now());
  const pct = timerDurationMs > 0 ? (remaining / timerDurationMs) * 100 : 0;
  timerFillEl.style.width = `${pct}%`;
  timerFillEl.classList.toggle("timer-fill--warn", pct <= 40 && pct > 15);
  timerFillEl.classList.toggle("timer-fill--danger", pct <= 15);
  if (remaining <= 0) clearInterval(timerInterval);
}

export function startTimer(durationMs, fillEl) {
  clearInterval(timerInterval);
  timerFillEl = fillEl;
  timerDurationMs = durationMs;
  timerEndAt = Date.now() + durationMs;
  timerPausedRemainingMs = null;
  timerInterval = setInterval(updateTimer, 100);
  updateTimer();
}

export function pauseTimer() {
  if (timerEndAt === null) return;
  timerPausedRemainingMs = Math.max(0, timerEndAt - Date.now());
  clearInterval(timerInterval);
}

export function resumeTimer() {
  if (timerPausedRemainingMs === null) return;
  timerEndAt = Date.now() + timerPausedRemainingMs;
  timerPausedRemainingMs = null;
  timerInterval = setInterval(updateTimer, 100);
}

export function stopTimer() {
  clearInterval(timerInterval);
  timerEndAt = null;
  timerPausedRemainingMs = null;
  if (timerFillEl) timerFillEl.style.width = "0%";
}

// Nécessaires à display-bridge.js:syncDisplay (transmet un timestamp absolu à l'écran public,
// plutôt qu'une durée restante, pour rester robuste à un léger délai de livraison du message).
export function timerEndAtCourant() {
  return timerEndAt;
}

export function timerDurationMsCourante() {
  return timerDurationMs;
}
