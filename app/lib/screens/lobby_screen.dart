import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/game_connection.dart';

class LobbyScreen extends StatelessWidget {
  const LobbyScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();

    return Padding(
      padding: const EdgeInsets.all(24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text("Salle d'attente", style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 8),
          Text('Code de la partie : ${game.gameCode ?? ''}', style: Theme.of(context).textTheme.titleMedium),
          if (game.teams.isNotEmpty) ...[
            const SizedBox(height: 20),
            Text('Ton équipe', style: Theme.of(context).textTheme.titleSmall),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final equipe in game.teams)
                  ChoiceChip(
                    label: Text(equipe.nom),
                    selected: game.teamId == equipe.id,
                    onSelected: (_) => context.read<GameConnection>().joinTeam(equipe.id),
                  ),
              ],
            ),
          ],
          const SizedBox(height: 24),
          Text('Joueurs connectés', style: Theme.of(context).textTheme.titleSmall),
          const SizedBox(height: 8),
          Expanded(
            child: game.players.isEmpty
                ? const Center(child: Text('En attente des autres joueurs...'))
                : ListView.builder(
                    itemCount: game.players.length,
                    itemBuilder: (context, index) {
                      final p = game.players[index];
                      final nomEquipe = game.teams
                          .where((t) => t.id == p.teamId)
                          .map((t) => t.nom)
                          .firstOrNull;
                      final sousTitre = [
                        ?nomEquipe,
                        if (!p.estConnecte) 'déconnecté',
                      ].join(' — ');
                      return ListTile(
                        leading: Icon(
                          Icons.circle,
                          color: p.estConnecte ? Colors.green : Colors.grey,
                          size: 12,
                        ),
                        title: Text(p.nom),
                        subtitle: sousTitre.isEmpty ? null : Text(sousTitre),
                      );
                    },
                  ),
          ),
          const SizedBox(height: 12),
          const Text(
            'En attente que le host démarre le round...',
            style: TextStyle(fontStyle: FontStyle.italic),
          ),
        ],
      ),
    );
  }
}
