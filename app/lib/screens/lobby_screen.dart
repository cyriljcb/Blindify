import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/game_connection.dart';
import '../theme.dart';
import '../widgets/game_card.dart';
import '../widgets/player_avatar.dart';

class LobbyScreen extends StatelessWidget {
  const LobbyScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();

    return GameCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text("Salle d'attente", style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 12),
          Container(
            width: double.infinity,
            padding: const EdgeInsets.symmetric(vertical: 14),
            decoration: BoxDecoration(
              color: BlindifyColors.surfaceAlt,
              borderRadius: BorderRadius.circular(14),
              border: Border.all(color: BlindifyColors.border),
            ),
            child: Column(
              children: [
                Text('CODE', style: Theme.of(context).textTheme.titleSmall),
                const SizedBox(height: 2),
                ShaderMask(
                  shaderCallback: (bounds) => const LinearGradient(
                    colors: [BlindifyColors.accent, BlindifyColors.accent2],
                  ).createShader(bounds),
                  child: Text(
                    game.gameCode ?? '',
                    style: const TextStyle(fontSize: 40, fontWeight: FontWeight.w900, letterSpacing: 6, color: Colors.white),
                  ),
                ),
              ],
            ),
          ),
          if (game.teams.isNotEmpty) ...[
            const SizedBox(height: 20),
            Text('TON ÉQUIPE', style: Theme.of(context).textTheme.titleSmall),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final equipe in game.teams)
                  ChoiceChip(
                    label: Text(equipe.nom),
                    selected: game.teamId == equipe.id,
                    selectedColor: BlindifyColors.accent,
                    labelStyle: TextStyle(
                      fontWeight: FontWeight.w700,
                      color: game.teamId == equipe.id ? BlindifyColors.onAccent : BlindifyColors.text,
                    ),
                    onSelected: (_) => context.read<GameConnection>().joinTeam(equipe.id),
                  ),
              ],
            ),
          ],
          const SizedBox(height: 20),
          Text('JOUEURS CONNECTÉS (${game.players.length})', style: Theme.of(context).textTheme.titleSmall),
          const SizedBox(height: 8),
          Expanded(
            child: game.players.isEmpty
                ? Center(
                    child: Text('En attente des autres joueurs...', style: Theme.of(context).textTheme.bodyMedium),
                  )
                : ListView.separated(
                    itemCount: game.players.length,
                    separatorBuilder: (context, index) => const SizedBox(height: 8),
                    itemBuilder: (context, index) {
                      final p = game.players[index];
                      final nomEquipe = game.teams.where((t) => t.id == p.teamId).map((t) => t.nom).firstOrNull;
                      final sousTitre = [
                        ?nomEquipe,
                        if (!p.estConnecte) 'déconnecté',
                      ].join(' — ');

                      return Container(
                        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                        decoration: BoxDecoration(
                          color: BlindifyColors.surfaceAlt,
                          borderRadius: BorderRadius.circular(12),
                          border: Border.all(color: BlindifyColors.border),
                        ),
                        child: Opacity(
                          opacity: p.estConnecte ? 1 : 0.5,
                          child: Row(
                            children: [
                              PlayerAvatar(id: p.playerId, nom: p.nom, size: 34),
                              const SizedBox(width: 12),
                              Expanded(
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text(p.nom, style: const TextStyle(fontWeight: FontWeight.w700)),
                                    if (sousTitre.isNotEmpty)
                                      Text(sousTitre, style: Theme.of(context).textTheme.bodySmall),
                                  ],
                                ),
                              ),
                            ],
                          ),
                        ),
                      );
                    },
                  ),
          ),
          const SizedBox(height: 12),
          Text(
            'En attente que le host démarre le round...',
            style: Theme.of(context).textTheme.bodySmall?.copyWith(fontStyle: FontStyle.italic),
          ),
        ],
      ),
    );
  }
}
