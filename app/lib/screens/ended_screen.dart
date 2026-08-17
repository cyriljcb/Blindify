import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/game_connection.dart';
import '../theme.dart';
import '../widgets/game_card.dart';
import '../widgets/player_avatar.dart';

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

    return GameCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Text('🏆', style: TextStyle(fontSize: 26)),
              const SizedBox(width: 8),
              Text('Partie terminée', style: Theme.of(context).textTheme.headlineSmall),
            ],
          ),
          const SizedBox(height: 16),
          Expanded(
            child: ListView.separated(
              itemCount: joueurs.length,
              separatorBuilder: (context, index) => const SizedBox(height: 8),
              itemBuilder: (context, index) {
                final j = joueurs[index];
                final estTop3 = index < 3;
                final couleurMedaille = switch (index) {
                  0 => const Color(0xFFD4AF37),
                  1 => const Color(0xFFB8B8C8),
                  2 => const Color(0xFFCD7F32),
                  _ => BlindifyColors.border,
                };

                return Container(
                  padding: EdgeInsets.symmetric(horizontal: 14, vertical: estTop3 ? 14 : 10),
                  decoration: BoxDecoration(
                    color: estTop3 ? couleurMedaille.withValues(alpha: 0.12) : BlindifyColors.surfaceAlt,
                    borderRadius: BorderRadius.circular(14),
                    border: Border.all(color: couleurMedaille),
                  ),
                  child: Row(
                    children: [
                      PlayerAvatar(id: j.playerId, nom: j.nom, medaille: index, size: estTop3 ? 40 : 34),
                      const SizedBox(width: 12),
                      Expanded(
                        child: Text(
                          j.nom,
                          style: TextStyle(fontWeight: FontWeight.w700, fontSize: estTop3 ? 17 : 15),
                        ),
                      ),
                      Text(
                        '${j.score}',
                        style: TextStyle(fontWeight: FontWeight.w800, fontSize: estTop3 ? 20 : 16),
                      ),
                    ],
                  ),
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}
