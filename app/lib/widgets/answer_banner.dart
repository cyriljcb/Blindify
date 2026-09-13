import 'package:flutter/material.dart';
import 'package:flutter_animate/flutter_animate.dart';

import '../motion.dart';

/// Bannière pause/réponse-envoyée/mode-course — dupliquée à l'identique dans round_screen.dart,
/// bonus_question_screen.dart et bonus_stake_screen.dart avant la fusion (docs/refactor-decisions.md
/// section 4). Garde la marge basse des deux premières copies (bonus_stake_screen.dart en manquait).
class AnswerBanner extends StatelessWidget {
  const AnswerBanner({super.key, required this.text, required this.color});

  final String text;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(12),
      margin: const EdgeInsets.only(bottom: 8),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.15),
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: color.withValues(alpha: 0.4)),
      ),
      child: Text(text, style: TextStyle(color: color, fontWeight: FontWeight.w600)),
      // Insérée/retirée conditionnellement par l'écran appelant (if game.paused / roundAnswered) —
      // chaque apparition est donc un nouvel Element, l'entrée rejoue à chaque fois.
    ).animate().fadeIn(duration: BlindifyMotion.fast).slideY(begin: -0.4, curve: BlindifyMotion.pop);
  }
}
