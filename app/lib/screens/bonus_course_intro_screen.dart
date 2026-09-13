import 'package:flutter/material.dart';
import 'package:flutter_animate/flutter_animate.dart';

import '../motion.dart';
import '../theme.dart';
import '../widgets/game_card.dart';

/// Écran forcé (~2,5s, voir GameConnection._dureeIntroCourse) affiché avant la vraie question
/// bonus quand BonusQuestionStarted.estCourse est vrai — retour utilisateur : une simple bannière
/// sur l'écran de question passait inaperçue, au point de ne pas savoir que ce mode était actif.
/// Entrée volontairement énergique (icône qui claque, titre qui rebondit, halo pulsé en fond) —
/// c'est littéralement l'écran "impossible à manquer", il doit se comporter comme tel.
class BonusCourseIntroScreen extends StatelessWidget {
  const BonusCourseIntroScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return GameCard(
      accentColor: BlindifyColors.mustard,
      child: Stack(
        alignment: Alignment.center,
        children: [
          // Halo pulsé derrière le contenu — juste un cercle flouté qui respire, pas une vraie
          // particule (confetti réservé aux moments de résultat, pas à une annonce de règle).
          Container(
            width: 220,
            height: 220,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: BlindifyColors.mustard.withValues(alpha: 0.16),
            ),
          ).animate(onPlay: (c) => c.repeat(reverse: true)).scaleXY(
                end: 1.15,
                duration: const Duration(milliseconds: 900),
                curve: Curves.easeInOut,
              ),
          Column(
            mainAxisAlignment: MainAxisAlignment.center,
            crossAxisAlignment: CrossAxisAlignment.center,
            children: [
              const Icon(Icons.bolt_rounded, color: BlindifyColors.mustard, size: 64)
                  .animate()
                  .scale(begin: const Offset(0.3, 0.3), duration: BlindifyMotion.slow, curve: BlindifyMotion.bounce)
                  .then()
                  .shake(hz: 4, duration: 400.ms, curve: Curves.easeInOut),
              const SizedBox(height: 16),
              Text(
                'MODE COURSE',
                style: Theme.of(context).textTheme.displaySmall?.copyWith(color: BlindifyColors.mustard),
                textAlign: TextAlign.center,
              )
                  .animate()
                  .fadeIn(duration: BlindifyMotion.fast)
                  .scale(begin: const Offset(0.7, 0.7), delay: 120.ms, curve: BlindifyMotion.bounce),
              const SizedBox(height: 16),
              Text(
                'Le premier qui répond décide — juste, il rafle la mise ; faux, il la perd.',
                style: Theme.of(context).textTheme.titleMedium,
                textAlign: TextAlign.center,
              ).animate().fadeIn(delay: 250.ms, duration: BlindifyMotion.normal).slideY(begin: 0.3),
              const SizedBox(height: 8),
              Text(
                'Tant que personne n\'a répondu, tu ne risques rien.',
                style: Theme.of(context).textTheme.bodyMedium,
                textAlign: TextAlign.center,
              ).animate().fadeIn(delay: 380.ms, duration: BlindifyMotion.normal).slideY(begin: 0.3),
            ],
          ),
        ],
      ),
    );
  }
}
