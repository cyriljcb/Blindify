import 'package:flutter/material.dart';

import '../theme.dart';
import '../widgets/game_card.dart';

/// Écran forcé (~2,5s, voir GameConnection._dureeIntroCourse) affiché avant la vraie question
/// bonus quand BonusQuestionStarted.estCourse est vrai — retour utilisateur : une simple bannière
/// sur l'écran de question passait inaperçue, au point de ne pas savoir que ce mode était actif.
class BonusCourseIntroScreen extends StatelessWidget {
  const BonusCourseIntroScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return GameCard(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          const Icon(Icons.bolt_rounded, color: BlindifyColors.mustard, size: 64),
          const SizedBox(height: 16),
          Text(
            'MODE COURSE',
            style: Theme.of(context).textTheme.displaySmall?.copyWith(color: BlindifyColors.mustard),
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 16),
          Text(
            'Le premier qui répond décide — juste, il rafle la mise ; faux, il la perd.',
            style: Theme.of(context).textTheme.titleMedium,
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 8),
          Text(
            'Tant que personne n\'a répondu, tu ne risques rien.',
            style: Theme.of(context).textTheme.bodyMedium,
            textAlign: TextAlign.center,
          ),
        ],
      ),
    );
  }
}
