import 'package:flutter/material.dart';
import 'package:flutter_animate/flutter_animate.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:provider/provider.dart';

import '../motion.dart';
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
              borderRadius: BorderRadius.circular(8),
              border: Border.all(color: BlindifyColors.ink, width: 3),
              boxShadow: hardShadow(BlindifyColors.mustard),
            ),
            child: Column(
              children: [
                Text('CODE', style: Theme.of(context).textTheme.titleSmall),
                const SizedBox(height: 2),
                Text(
                  game.gameCode ?? '',
                  style: TextStyle(
                    fontFamily: GoogleFonts.anton().fontFamily,
                    fontSize: 40,
                    fontWeight: FontWeight.w400,
                    letterSpacing: 6,
                    color: BlindifyColors.ink,
                  ),
                  // Pulsation discrète en continu — invite à regarder ce bloc plutôt que de le
                  // laisser complètement immobile pendant toute l'attente en salle.
                ).animate(onPlay: (c) => c.repeat(reverse: true)).scaleXY(
                      end: 1.04,
                      duration: const Duration(milliseconds: 1100),
                      curve: Curves.easeInOut,
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
                    selectedColor: BlindifyColors.cobalt,
                    labelStyle: TextStyle(
                      fontWeight: FontWeight.w700,
                      color: game.teamId == equipe.id ? BlindifyColors.onAccent : BlindifyColors.ink,
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
                          borderRadius: BorderRadius.circular(8),
                          border: Border.all(color: BlindifyColors.ink, width: 2),
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
                      )
                          // Sans clé explicite : Flutter réutilise l'Element existant pour un joueur
                          // déjà affiché (même index), donc l'entrée ne rejoue pas à chaque
                          // PlayerJoined — seul un NOUVEL index (joueur qui vient de rejoindre)
                          // obtient un Element frais et voit l'animation.
                          .animate()
                          .fadeIn(duration: BlindifyMotion.normal)
                          .slideX(begin: 0.15, curve: BlindifyMotion.pop);
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
