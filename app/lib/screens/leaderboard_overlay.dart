import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../models/score_update.dart';
import '../services/game_connection.dart';
import '../theme.dart';
import '../widgets/player_avatar.dart';

class LeaderboardOverlay extends StatelessWidget {
  const LeaderboardOverlay({super.key});

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();
    final leaderboard = game.leaderboard;

    return Positioned.fill(
      child: ColoredBox(
        color: Colors.black.withValues(alpha: 0.7),
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 400, maxHeight: 520),
            child: Container(
              margin: const EdgeInsets.all(24),
              padding: const EdgeInsets.all(20),
              decoration: BoxDecoration(
                color: BlindifyColors.surface,
                borderRadius: BorderRadius.circular(20),
                border: Border.all(color: BlindifyColors.border),
              ),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Row(
                    children: [
                      const Text('📊', style: TextStyle(fontSize: 22)),
                      const SizedBox(width: 8),
                      Expanded(child: Text('Tableau général', style: Theme.of(context).textTheme.headlineSmall)),
                    ],
                  ),
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
        for (var i = 0; i < joueurs.length; i++) _ScoreRow(id: joueurs[i].playerId, nom: joueurs[i].nom, score: joueurs[i].score, rang: i),
        if (equipes != null && equipes.isNotEmpty) ...[
          const Divider(),
          Text('ÉQUIPES', style: Theme.of(context).textTheme.titleSmall),
          const SizedBox(height: 8),
          for (var i = 0; i < equipes.length; i++)
            _ScoreRow(id: equipes[i].teamId, nom: equipes[i].nom, score: equipes[i].score, rang: i),
        ],
      ],
    );
  }
}

class _ScoreRow extends StatelessWidget {
  const _ScoreRow({required this.id, required this.nom, required this.score, required this.rang});

  final String id;
  final String nom;
  final int score;
  final int rang;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Row(
        children: [
          PlayerAvatar(id: id, nom: nom, medaille: rang, size: 30),
          const SizedBox(width: 10),
          Expanded(child: Text(nom, style: const TextStyle(fontWeight: FontWeight.w600))),
          Text('$score', style: const TextStyle(fontWeight: FontWeight.w800)),
        ],
      ),
    );
  }
}
