import 'dart:math';

import 'package:confetti/confetti.dart';
import 'package:flutter/material.dart';
import 'package:flutter_animate/flutter_animate.dart';
import 'package:provider/provider.dart';

import '../motion.dart';
import '../services/game_connection.dart';
import '../theme.dart';
import '../widgets/game_card.dart';
import '../widgets/player_avatar.dart';

class EndedScreen extends StatefulWidget {
  const EndedScreen({super.key});

  @override
  State<EndedScreen> createState() => _EndedScreenState();
}

class _EndedScreenState extends State<EndedScreen> {
  late final ConfettiController _confetti;

  @override
  void initState() {
    super.initState();
    // Fin de partie = toujours une occasion de fête, contrairement à un round classique où
    // seule une bonne réponse déclenche les confettis (voir RoundEndedScreen) — pas de condition
    // ici, tout le monde a fini la partie.
    _confetti = ConfettiController(duration: const Duration(milliseconds: 1400))..play();
  }

  @override
  void dispose() {
    _confetti.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();
    final scores = game.finalScores;

    if (scores == null) {
      return const Center(child: Text('Partie terminée.'));
    }

    final joueurs = [...scores.joueurs]..sort((a, b) => b.score.compareTo(a.score));
    // Titre de repli, décerné à qui n'a rien gagné d'autre — affiché en dernier, pas mis en avant
    // au même titre qu'un vrai fait d'armes (voir TitresService, section 12.6).
    final titresTries = [
      ...game.finalTitres.where((t) => t.code != 'FIDELE'),
      ...game.finalTitres.where((t) => t.code == 'FIDELE'),
    ];

    return Stack(
      alignment: Alignment.topCenter,
      children: [
        GameCard(
          accentColor: BlindifyColors.mustard,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  const Text('🏆', style: TextStyle(fontSize: 26))
                      .animate()
                      .scale(begin: Offset.zero, duration: BlindifyMotion.slow, curve: BlindifyMotion.bounce),
                  const SizedBox(width: 8),
                  Text('Partie terminée', style: Theme.of(context).textTheme.headlineSmall),
                ],
              ),
              const SizedBox(height: 16),
              Expanded(
                child: ListView.separated(
                  itemCount: joueurs.length + (titresTries.isEmpty ? 0 : titresTries.length + 1),
                  separatorBuilder: (context, index) => const SizedBox(height: 8),
                  itemBuilder: (context, index) {
                    if (index >= joueurs.length) {
                      final indexTitre = index - joueurs.length;
                      if (indexTitre == 0) {
                        return Padding(
                          padding: const EdgeInsets.only(top: 8),
                          child: Text('🎖️ Titres de la partie', style: Theme.of(context).textTheme.titleMedium),
                        ).animate(delay: Duration(milliseconds: 120 * (joueurs.length + 1))).fadeIn(duration: BlindifyMotion.normal);
                      }

                      final titre = titresTries[indexTitre - 1];
                      final estAMoi = game.playerId != null && titre.playerIds.contains(game.playerId);

                      return Container(
                        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                        decoration: BoxDecoration(
                          color: estAMoi ? BlindifyColors.mustard.withValues(alpha: 0.15) : BlindifyColors.surfaceAlt,
                          borderRadius: BorderRadius.circular(8),
                          border: Border.all(color: estAMoi ? BlindifyColors.mustard : BlindifyColors.ink, width: estAMoi ? 3 : 1),
                        ),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(titre.libelle, style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 15)),
                            const SizedBox(height: 2),
                            Text(titre.description, style: Theme.of(context).textTheme.bodySmall),
                            const SizedBox(height: 4),
                            Wrap(
                              spacing: 6,
                              runSpacing: 4,
                              children: titre.playerIds
                                  .map((id) => joueurs.where((j) => j.playerId == id).map((j) => j.nom).firstOrNull ?? id)
                                  .map((nom) => Chip(
                                        label: Text(nom, style: const TextStyle(fontSize: 12)),
                                        visualDensity: VisualDensity.compact,
                                        materialTapTargetSize: MaterialTapTargetSize.shrinkWrap,
                                      ))
                                  .toList(),
                            ),
                          ],
                        ),
                      ).animate(delay: Duration(milliseconds: 120 * (joueurs.length + 1 + indexTitre))).fadeIn(duration: BlindifyMotion.normal).slideY(begin: 0.2, curve: BlindifyMotion.pop);
                    }

                    final j = joueurs[index];
                    final estTop3 = index < 3;
                    final couleurMedaille = switch (index) {
                      0 => const Color(0xFFD4AF37),
                      1 => const Color(0xFFB8B8C8),
                      2 => const Color(0xFFCD7F32),
                      _ => BlindifyColors.ink,
                    };

                    return Container(
                      padding: EdgeInsets.symmetric(horizontal: 14, vertical: estTop3 ? 14 : 10),
                      decoration: BoxDecoration(
                        color: estTop3 ? couleurMedaille.withValues(alpha: 0.12) : BlindifyColors.surfaceAlt,
                        borderRadius: BorderRadius.circular(8),
                        border: Border.all(color: couleurMedaille, width: estTop3 ? 3 : 2),
                        boxShadow: estTop3 ? hardShadow(couleurMedaille, offset: 4) : null,
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
                    )
                        // Podium révélé du bas du classement vers le haut (le 1er arrive en dernier,
                        // comme une remise de prix) plutôt que dans l'ordre de la liste.
                        .animate(delay: Duration(milliseconds: 120 * (joueurs.length - index)))
                        .fadeIn(duration: BlindifyMotion.normal)
                        .slideY(begin: 0.3, curve: BlindifyMotion.pop);
                  },
                ),
              ),
            ],
          ),
        ),
        Align(
          alignment: Alignment.topCenter,
          child: ConfettiWidget(
            confettiController: _confetti,
            blastDirection: pi / 2,
            blastDirectionality: BlastDirectionality.explosive,
            numberOfParticles: 40,
            maxBlastForce: 22,
            minBlastForce: 10,
            gravity: 0.35,
            shouldLoop: false,
            colors: const [BlindifyColors.mustard, BlindifyColors.good, BlindifyColors.cobalt, BlindifyColors.coral],
          ),
        ),
      ],
    );
  }
}
