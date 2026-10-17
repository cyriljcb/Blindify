import 'package:flutter/services.dart';

/// Retours haptiques du jeu (refonte UI, lot 1) — on joue souvent les yeux levés vers l'écran
/// public, la vibration confirme l'action sans avoir à regarder le téléphone. Uniquement l'API
/// HapticFeedback du SDK (aucune dépendance ajoutée) : sur Android, elle respecte le réglage
/// système « retour tactile » — désactivé là-bas, rien ne vibre, sans erreur.
class Haptique {
  Haptique._();

  /// Réponse ou mise envoyée.
  static void validation() => HapticFeedback.mediumImpact();

  /// Double impulsion : bonne réponse.
  static Future<void> bonneReponse() async {
    await HapticFeedback.heavyImpact();
    await Future<void>.delayed(const Duration(milliseconds: 140));
    await HapticFeedback.heavyImpact();
  }

  /// Vibration longue : mauvaise réponse ou absence de réponse.
  static void erreur() => HapticFeedback.vibrate();

  /// Tic léger sur chacune des dernières secondes du chrono.
  static void tic() => HapticFeedback.selectionClick();

  /// Deux impulsions légères : un round, une mise ou une question bonus démarre (retour
  /// utilisateur 2026-10-17 — téléphone posé ou en poche, on rate le début du round).
  static Future<void> depart() async {
    await HapticFeedback.lightImpact();
    await Future<void>.delayed(const Duration(milliseconds: 110));
    await HapticFeedback.lightImpact();
  }

  /// Impulsion forte unique quand le chrono passe au rouge sans réponse envoyée (même seuil que
  /// TimerBar : 15 % du temps restant).
  static void alerte() => HapticFeedback.heavyImpact();
}
