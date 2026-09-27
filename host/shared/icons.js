"use strict";

// Refonte UI (lot 2) — petites icônes SVG en ligne, à la place des emojis (🏁 ⏸ ✨ 🎵 🥇…) dont le
// rendu variait d'un navigateur/OS à l'autre. Même esprit que les icônes Material côté Flutter
// (flag, pause, auto_awesome, music_note) : formes pleines, couleur héritée (currentColor).
const CHEMINS = {
  drapeau: '<path d="M5 21V4h11l-1.5 3.5L16 11H7v10z"/>',
  pause: '<path d="M7 5h3.5v14H7zM13.5 5H17v14h-3.5z"/>',
  etincelle: '<path d="M12 2l2.2 6.3L20.5 10.5l-6.3 2.2L12 19l-2.2-6.3L3.5 10.5l6.3-2.2zM19 16l.9 2.1L22 19l-2.1.9L19 22l-.9-2.1L16 19l2.1-.9z"/>',
  note: '<path d="M9 3v11.3A3.5 3.5 0 1 0 11 17.5V8h7V3z"/>',
};

export function iconeSvg(nom, classe = "icone") {
  return `<svg class="${classe}" viewBox="0 0 24 24" aria-hidden="true" fill="currentColor">${CHEMINS[nom] ?? ""}</svg>`;
}
