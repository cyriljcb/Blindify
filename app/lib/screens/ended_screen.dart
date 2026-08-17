import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/game_connection.dart';

class EndedScreen extends StatelessWidget {
  const EndedScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();
    final scores = game.finalScores;

    if (scores == null) {
      return const Center(child: Text('Partie terminée.'));
    }

    final joueurs = [...scores.joueurs]..sort((a, b) => b.score.compareTo(a.score));

    return Padding(
      padding: const EdgeInsets.all(24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Partie terminée', style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 16),
          Expanded(
            child: ListView.builder(
              itemCount: joueurs.length,
              itemBuilder: (context, index) {
                final j = joueurs[index];
                return ListTile(
                  leading: CircleAvatar(child: Text('${index + 1}')),
                  title: Text(j.nom),
                  trailing: Text('${j.score} pts', style: Theme.of(context).textTheme.titleMedium),
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}
