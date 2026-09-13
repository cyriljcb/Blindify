import 'package:flutter/material.dart';
import 'package:flutter_animate/flutter_animate.dart';
import 'package:provider/provider.dart';

import '../motion.dart';
import '../services/game_connection.dart';
import '../widgets/game_card.dart';
import '../widgets/serie_badge.dart';

/// Annonce de série (retour utilisateur du 2026-08-24) — reste affiché jusqu'à l'événement suivant
/// (RoundStarted ou BonusStakeOptions), envoyé par le serveur seulement une fois le host prêt à
/// enchaîner : pas de minuteur local ici, la disparition de cet écran est pilotée par ce prochain
/// événement plutôt que par un délai fixe côté app.
class SerieIntroScreen extends StatelessWidget {
  const SerieIntroScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final serieIntro = context.watch<GameConnection>().serieIntro;

    return GameCard(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Text('Prochaine série', style: Theme.of(context).textTheme.bodyMedium)
              .animate()
              .fadeIn(duration: BlindifyMotion.fast),
          const SizedBox(height: 12),
          Text(
            serieIntro != null ? 'Série ${lettreSerie(serieIntro.serieIndex)}' : '',
            style: Theme.of(context).textTheme.displaySmall,
            textAlign: TextAlign.center,
          ).animate().fadeIn(delay: 100.ms).scale(begin: const Offset(0.6, 0.6), curve: BlindifyMotion.bounce),
          const SizedBox(height: 8),
          Text(
            serieIntro != null ? libelleTheme(serieIntro.tags) : '',
            style: Theme.of(context).textTheme.titleMedium,
            textAlign: TextAlign.center,
          ).animate().fadeIn(delay: 300.ms, duration: BlindifyMotion.normal).slideY(begin: 0.3),
        ],
      ),
    );
  }
}
