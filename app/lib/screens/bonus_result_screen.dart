import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/game_connection.dart';

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

    return Padding(
      padding: const EdgeInsets.all(24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Résultat de la question bonus', style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 8),
          Text('${result.title} — ${result.artist}', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 24),
          if (monResultat != null) ...[
            Icon(
              monResultat.estCorrecte ? Icons.check_circle : Icons.cancel,
              color: monResultat.estCorrecte ? Colors.green : Colors.red,
              size: 48,
            ),
            const SizedBox(height: 8),
            Text(
              monResultat.estCorrecte ? '+${monResultat.points} points !' : '${monResultat.points} points',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            const SizedBox(height: 4),
            Text('Mise : ${monResultat.mise} pts'),
          ],
          const Spacer(),
          const Text('En attente de la fin de la partie...', style: TextStyle(fontStyle: FontStyle.italic)),
        ],
      ),
    );
  }
}
