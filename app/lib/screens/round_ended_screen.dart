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

class RoundEndedScreen extends StatefulWidget {
  const RoundEndedScreen({super.key});

  @override
  State<RoundEndedScreen> createState() => _RoundEndedScreenState();
}

class _RoundEndedScreenState extends State<RoundEndedScreen> {
  late final ConfettiController _confetti;

  @override
  void initState() {
    super.initState();
    _confetti = ConfettiController(duration: const Duration(milliseconds: 700));

    final game = context.read<GameConnection>();
    final result = game.lastRoundResult;
    final mine = result?.resultats.where((r) => r.playerId == game.playerId);
    final monResultat = mine != null && mine.isNotEmpty ? mine.first : null;
    // Déclenché une seule fois à l'arrivée sur cet écran (nouvelle State à chaque round, voir
    // main.dart: KeyedSubtree(key: ValueKey(game.screen))) — jamais rejoué sur un simple rebuild
    // (ScoreUpdate, etc.) puisque initState ne s'exécute qu'à la création.
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
    final result = game.lastRoundResult;

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
              Text('Résultat', style: Theme.of(context).textTheme.headlineSmall),
              const SizedBox(height: 16),
              CoverArt(imageUrl: game.coverUrl(result.coverPath))
                  .animate()
                  .fadeIn(duration: BlindifyMotion.normal)
                  .scale(begin: const Offset(0.7, 0.7), curve: BlindifyMotion.bounce),
              const SizedBox(height: 16),
              // Titre toujours au-dessus de l'artiste, quelle que soit la cible du round — même ordre
              // que l'écran public (host/shared/format.js:libelleReveal), pour que les deux écrans se
              // lisent pareil pendant une partie.
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
                _ResultIcon(correct: correct),
                const SizedBox(height: 8),
                Text(
                  correct ? 'Bonne réponse !' : 'Mauvaise réponse',
                  style: Theme.of(context).textTheme.titleLarge,
                ).animate().fadeIn(delay: 400.ms),
                _PointsCountUp(points: monResultat.points, color: correct ? BlindifyColors.good : BlindifyColors.bad),
                // Cible Année (V2, section 12.5) : affiche l'écart en plus du résultat brut — sans
                // ça, "mauvaise réponse" ne dit pas si on était loin ou à un an près.
                if (monResultat.ecartAnnee != null) ...[
                  const SizedBox(height: 4),
                  Text(
                    monResultat.ecartAnnee == 0
                        ? 'Année exacte !'
                        : 'à ${monResultat.ecartAnnee} an${monResultat.ecartAnnee! > 1 ? 's' : ''} près',
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ],
              ],
              const Spacer(),
              Text(
                'En attente du prochain round...',
                style: Theme.of(context).textTheme.bodySmall?.copyWith(fontStyle: FontStyle.italic),
              ),
            ],
          ),
        ),
        // Confettis tirés depuis le haut de l'écran — pas depuis l'icône (qui bouge encore à cet
        // instant à cause de son propre .animate()), pour un rendu net indépendant du reste.
        Align(
          alignment: Alignment.topCenter,
          child: ConfettiWidget(
            confettiController: _confetti,
            blastDirection: pi / 2,
            blastDirectionality: BlastDirectionality.explosive,
            numberOfParticles: 24,
            maxBlastForce: 18,
            minBlastForce: 8,
            gravity: 0.4,
            shouldLoop: false,
            colors: const [BlindifyColors.mustard, BlindifyColors.good, BlindifyColors.cobalt, BlindifyColors.coral],
          ),
        ),
      ],
    );
  }
}

/// Icône de résultat : rebond élastique à l'arrivée, suivi d'un petit tremblement uniquement en cas
/// de mauvaise réponse (le rebond seul suffit à célébrer une bonne réponse — pas besoin de secouer
/// aussi quelque chose de positif).
class _ResultIcon extends StatelessWidget {
  const _ResultIcon({required this.correct});

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

/// Compteur de points qui défile de 0 jusqu'à la valeur finale plutôt que de l'afficher d'un coup
/// — retour utilisateur : les résultats "tombaient" sans aucun suspense.
class _PointsCountUp extends StatelessWidget {
  const _PointsCountUp({required this.points, required this.color});

  final int points;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return TweenAnimationBuilder<double>(
      tween: Tween(begin: 0, end: points.toDouble()),
      duration: const Duration(milliseconds: 700),
      curve: Curves.easeOutCubic,
      builder: (context, value, _) {
        final v = value.round();
        return Text(
          '${v >= 0 ? '+' : ''}$v points',
          style: TextStyle(color: color, fontWeight: FontWeight.w800, fontSize: 18),
        );
      },
    );
  }
}
