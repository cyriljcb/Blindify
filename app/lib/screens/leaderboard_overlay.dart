import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../models/score_update.dart';
import '../services/game_connection.dart';

class LeaderboardOverlay extends StatelessWidget {
  const LeaderboardOverlay({super.key});

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();
    final leaderboard = game.leaderboard;

    return Positioned.fill(
      child: ColoredBox(
        color: Colors.black54,
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 400, maxHeight: 500),
            child: Card(
              margin: const EdgeInsets.all(24),
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Text('Tableau général', style: Theme.of(context).textTheme.headlineSmall),
                    const SizedBox(height: 12),
                    if (leaderboard != null) Flexible(child: _ScoreList(scores: leaderboard)),
                    const SizedBox(height: 12),
                    FilledButton(
                      onPressed: () => context.read<GameConnection>().closeLeaderboard(),
                      child: const Text('Fermer'),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _ScoreList extends StatelessWidget {
  const _ScoreList({required this.scores});

  final ScoreUpdate scores;

  @override
  Widget build(BuildContext context) {
    final joueurs = [...scores.joueurs]..sort((a, b) => b.score.compareTo(a.score));
    final equipes = scores.equipes == null ? null : ([...scores.equipes!]..sort((a, b) => b.score.compareTo(a.score)));

    return ListView(
      shrinkWrap: true,
      children: [
        for (final j in joueurs) ListTile(dense: true, title: Text(j.nom), trailing: Text('${j.score}')),
        if (equipes != null && equipes.isNotEmpty) ...[
          const Divider(),
          const ListTile(dense: true, title: Text('Équipes', style: TextStyle(fontWeight: FontWeight.bold))),
          for (final eq in equipes) ListTile(dense: true, title: Text(eq.nom), trailing: Text('${eq.score}')),
        ],
      ],
    );
  }
}
