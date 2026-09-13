import 'package:flutter/animation.dart';

/// Constantes de mouvement partagées — retour utilisateur : l'app manquait de dynamisme
/// (transitions plates, aucun retour tactile). Courbes "à ressort" (overshoot/elastic) plutôt que
/// les easings Material par défaut, dans l'esprit des apps de quiz/soirée qui ont cartonné
/// (Kahoot, HQ Trivia...) : mouvement rapide, punchy, jamais mou.
class BlindifyMotion {
  BlindifyMotion._();

  static const fast = Duration(milliseconds: 180);
  static const normal = Duration(milliseconds: 380);
  static const slow = Duration(milliseconds: 650);

  /// Léger dépassement avant de se stabiliser — utilisé pour les entrées de carte/tuile.
  static const pop = Curves.easeOutBack;

  /// Rebond plus prononcé — révélations (bonne/mauvaise réponse, podium).
  static const bounce = Curves.elasticOut;

  static const snappy = Curves.easeOutCubic;
}
