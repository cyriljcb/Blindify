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
}
