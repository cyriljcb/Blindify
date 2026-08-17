import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/game_connection.dart';
import '../theme.dart';
import '../widgets/cover_art.dart';
import '../widgets/game_card.dart';

class BonusResultScreen extends StatelessWidget {
  const BonusResultScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();
    final result = game.lastBonusResult;

    if (result == null) {
      return const Center(child: Text('En attente du résultat...'));
    }

    final mine = result.resultats.where((r) => r.playerId == game.playerId);
    final monResultat = mine.isNotEmpty ? mine.first : null;

    return GameCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Text('Résultat de la question bonus', style: Theme.of(context).textTheme.headlineSmall, textAlign: TextAlign.center),
          const SizedBox(height: 16),
          CoverArt(imageUrl: game.coverUrl(result.coverPath)),
          const SizedBox(height: 16),
          Text(result.title, textAlign: TextAlign.center, style: Theme.of(context).textTheme.titleLarge),
          Text(result.artist, textAlign: TextAlign.center, style: Theme.of(context).textTheme.bodyMedium),
          const SizedBox(height: 24),
          if (monResultat != null) ...[
            Icon(
              monResultat.estCorrecte ? Icons.check_circle_rounded : Icons.cancel_rounded,
              color: monResultat.estCorrecte ? BlindifyColors.good : BlindifyColors.bad,
              size: 48,
            ),
            const SizedBox(height: 8),
            Text(
              monResultat.estCorrecte ? '+${monResultat.points} points !' : '${monResultat.points} points',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            const SizedBox(height: 4),
            Text('Mise : ${monResultat.mise} pts', style: Theme.of(context).textTheme.bodySmall),
          ],
          const Spacer(),
          Text(
            'En attente de la fin de la partie...',
            style: Theme.of(context).textTheme.bodySmall?.copyWith(fontStyle: FontStyle.italic),
          ),
        ],
      ),
    );
  }
}
