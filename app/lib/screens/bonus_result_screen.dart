import 'dart:math';

import 'package:confetti/confetti.dart';
import 'package:flutter/material.dart';
import 'package:flutter_animate/flutter_animate.dart';
import 'package:provider/provider.dart';

import '../motion.dart';
import '../services/game_connection.dart';
import '../theme.dart';
import '../widgets/cover_art.dart';
import '../widgets/game_card.dart';

class BonusResultScreen extends StatefulWidget {
  const BonusResultScreen({super.key});

  @override
  State<BonusResultScreen> createState() => _BonusResultScreenState();
}

class _BonusResultScreenState extends State<BonusResultScreen> {
  late final ConfettiController _confetti;

  @override
  void initState() {
    super.initState();
    _confetti = ConfettiController(duration: const Duration(milliseconds: 900));

    final game = context.read<GameConnection>();
    final result = game.lastBonusResult;
    final mine = result?.resultats.where((r) => r.playerId == game.playerId);
    final monResultat = mine != null && mine.isNotEmpty ? mine.first : null;
    // Enjeu plus élevé qu'un round classique (mise en jeu) — confettis dès que la mise est
    // remportée, jamais rejoué sur un simple rebuild (voir RoundEndedScreen pour le même principe).
    if (monResultat?.estCorrecte == true) _confetti.play();
  }

  @override
  void dispose() {
    _confetti.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();
    final result = game.lastBonusResult;

    if (result == null) {
      return const Center(child: Text('En attente du résultat...'));
    }

    final mine = result.resultats.where((r) => r.playerId == game.playerId);
    final monResultat = mine.isNotEmpty ? mine.first : null;
    final correct = monResultat?.estCorrecte == true;

    return Stack(
      alignment: Alignment.topCenter,
      children: [
        GameCard(
          accentColor: monResultat == null ? null : (correct ? BlindifyColors.good : BlindifyColors.bad),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.center,
            children: [
              Text('Résultat de la question bonus', style: Theme.of(context).textTheme.headlineSmall, textAlign: TextAlign.center),
              const SizedBox(height: 16),
              CoverArt(imageUrl: game.coverUrl(result.coverPath))
                  .animate()
                  .fadeIn(duration: BlindifyMotion.normal)
                  .scale(begin: const Offset(0.7, 0.7), curve: BlindifyMotion.bounce),
              const SizedBox(height: 16),
              // Titre toujours au-dessus de l'artiste, quelle que soit la cible — même ordre que
              // l'écran public (host/shared/format.js:libelleReveal).
              Text(
                result.cible == 'Film'
                    ? result.film
                    : result.cible == 'Annee' && result.annee != null
                        ? '${result.annee}'
                        : result.title,
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.titleLarge,
              ).animate().fadeIn(delay: 150.ms, duration: BlindifyMotion.normal).slideY(begin: 0.3),
              Text(
                result.cible == 'Film' ? '${result.title} — ${result.artist}' : result.artist,
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.bodyMedium,
              ).animate().fadeIn(delay: 220.ms, duration: BlindifyMotion.normal).slideY(begin: 0.3),
              const SizedBox(height: 24),
              if (monResultat != null) ...[
                _BonusResultIcon(correct: correct),
                const SizedBox(height: 8),
                Text(
                  correct ? '+${monResultat.points} points !' : '${monResultat.points} points',
                  style: Theme.of(context).textTheme.titleLarge,
                ).animate().fadeIn(delay: 400.ms),
                const SizedBox(height: 4),
                Text('Mise : ${monResultat.mise} pts', style: Theme.of(context).textTheme.bodySmall)
                    .animate()
                    .fadeIn(delay: 500.ms),
                // Cible Année (V2, section 12.5) — voir RoundEndedScreen pour le même principe.
                if (monResultat.ecartAnnee != null) ...[
                  const SizedBox(height: 4),
                  Text(
                    monResultat.ecartAnnee == 0
                        ? 'Année exacte !'
                        : 'à ${monResultat.ecartAnnee} an${monResultat.ecartAnnee! > 1 ? 's' : ''} près',
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ],
              ] else if (result.estCourse) ...[
                const Icon(Icons.bolt_rounded, color: BlindifyColors.mustard, size: 48)
                    .animate()
                    .scale(begin: Offset.zero, delay: 280.ms, duration: BlindifyMotion.slow, curve: BlindifyMotion.bounce),
                const SizedBox(height: 8),
                const Text('Un autre joueur a répondu en premier — ta mise est récupérée intacte.', textAlign: TextAlign.center)
                    .animate()
                    .fadeIn(delay: 450.ms),
              ],
              const Spacer(),
              Text(
                'En attente de la fin de la partie...',
                style: Theme.of(context).textTheme.bodySmall?.copyWith(fontStyle: FontStyle.italic),
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
            numberOfParticles: 30,
            maxBlastForce: 20,
            minBlastForce: 9,
            gravity: 0.4,
            shouldLoop: false,
            colors: const [BlindifyColors.mustard, BlindifyColors.good, BlindifyColors.cobalt, BlindifyColors.coral],
          ),
        ),
      ],
    );
  }
}

class _BonusResultIcon extends StatelessWidget {
  const _BonusResultIcon({required this.correct});

  final bool correct;

  @override
  Widget build(BuildContext context) {
    final icon = Icon(
      correct ? Icons.check_circle_rounded : Icons.cancel_rounded,
      color: correct ? BlindifyColors.good : BlindifyColors.bad,
      size: 48,
    );
    final base = icon
        .animate()
        .scale(begin: Offset.zero, delay: 280.ms, duration: BlindifyMotion.slow, curve: BlindifyMotion.bounce);
    return correct ? base : base.then().shake(hz: 3, duration: 350.ms);
  }
}
