import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_animate/flutter_animate.dart';
import 'package:provider/provider.dart';

import '../motion.dart';
import '../services/game_connection.dart';
import '../theme.dart';
import '../widgets/answer_banner.dart';
import '../widgets/fill_height_list.dart';
import '../widgets/game_card.dart';
import '../widgets/serie_badge.dart';
import '../widgets/timer_bar.dart';

class BonusStakeScreen extends StatefulWidget {
  const BonusStakeScreen({super.key});

  @override
  State<BonusStakeScreen> createState() => _BonusStakeScreenState();
}

class _BonusStakeScreenState extends State<BonusStakeScreen> {
  Timer? _ticker;
  int _remainingMs = 0;

  @override
  void initState() {
    super.initState();
    final game = context.read<GameConnection>();
    // tempsEcouleMs > 0 après une reconnexion en pleine phase de mise — voir
    // AnswerPhaseScreen.initState pour la même logique côté round/question bonus.
    final options = game.bonusStakeOptions;
    _remainingMs = options == null ? 0 : (options.dureePhaseMiseMs - options.tempsEcouleMs).clamp(0, options.dureePhaseMiseMs);
    if (!game.paused) _startTicker();
  }

  void _startTicker() {
    _ticker?.cancel();
    _ticker = Timer.periodic(const Duration(milliseconds: 100), (_) {
      setState(() => _remainingMs = (_remainingMs - 100).clamp(0, _remainingMs));
      if (_remainingMs <= 0) _ticker?.cancel();
    });
  }

  // Idempotent, même pattern que RoundScreen (retour de test : le décompte visuel continuait de
  // tourner pendant une pause sur cet écran, alors que le round classique le gelait déjà).
  void _syncTickerWithPause(bool paused) {
    if (paused && _ticker != null) {
      _ticker?.cancel();
      _ticker = null;
    } else if (!paused && _ticker == null && _remainingMs > 0) {
      _startTicker();
    }
  }

  @override
  void dispose() {
    _ticker?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();
    final options = game.bonusStakeOptions;

    if (options == null) {
      return const Center(child: Text('En attente de la question bonus...'));
    }

    _syncTickerWithPause(game.paused);

    final disabled = game.bonusStakeEnvoyee || game.paused;
    final totalMs = options.dureePhaseMiseMs;
    final progress = totalMs > 0 ? _remainingMs / totalMs : 0.0;

    return GameCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.casino_rounded, color: BlindifyColors.mustard),
              const SizedBox(width: 8),
              Expanded(child: Text('Mise à l\'aveugle', style: Theme.of(context).textTheme.headlineSmall)),
              SerieBadge(serieIndex: options.serieIndex),
            ],
          ),
          const SizedBox(height: 8),
          Text(
            'Choisis un palier avant de découvrir la question. Pas de choix dans le délai = palier "safe" appliqué automatiquement.',
            style: Theme.of(context).textTheme.bodyMedium,
          ),
          const SizedBox(height: 12),
          TimerBar(progress: progress, secondesRestantes: (_remainingMs / 1000).ceil()),
          const SizedBox(height: 16),
          if (game.paused) const AnswerBanner(text: 'Partie en pause — en attente du host.', color: BlindifyColors.warn),
          if (game.bonusStakeEnvoyee && !game.paused)
            const AnswerBanner(text: 'Mise envoyée — en attente des autres joueurs.', color: BlindifyColors.good),
          const SizedBox(height: 12),
          Expanded(
            child: FillHeightList(
              itemCount: options.paliers.length,
              itemBuilder: (context, index) {
                final valeur = options.paliers[index];
                final selectionne = game.bonusPalierSelectionne == index;
                return Material(
                  color: selectionne ? BlindifyColors.cobalt : BlindifyColors.surfaceAlt,
                  borderRadius: BorderRadius.circular(8),
                  child: InkWell(
                    borderRadius: BorderRadius.circular(8),
                    onTap: disabled ? null : () => context.read<GameConnection>().selectStake(index),
                    child: AnimatedScale(
                      // Petit "clac" au choix d'un palier — même principe que _AnswerTile côté QCM.
                      scale: selectionne ? 1.05 : 1.0,
                      duration: BlindifyMotion.fast,
                      curve: BlindifyMotion.pop,
                      child: Container(
                        width: double.infinity,
                        alignment: Alignment.center,
                        // Centré plutôt qu'un padding fixe (16) : la tuile a désormais une hauteur
                        // calculée pour remplir l'espace disponible, parfois plus serrée qu'avant —
                        // un padding fixe y déborderait, alors qu'un centrage s'adapte à toute hauteur.
                        padding: const EdgeInsets.symmetric(horizontal: 16),
                        decoration: BoxDecoration(
                          borderRadius: BorderRadius.circular(8),
                          border: Border.all(color: BlindifyColors.ink, width: selectionne ? 3 : 2),
                        ),
                        child: Text(
                          'Palier ${index + 1}${index == 0 ? " (safe)" : ""} — $valeur pts',
                          textAlign: TextAlign.center,
                          style: TextStyle(
                            fontWeight: FontWeight.w700,
                            color: selectionne ? BlindifyColors.onAccent : BlindifyColors.ink,
                          ),
                        ),
                      ),
                    ),
                  ),
                ).animate(delay: Duration(milliseconds: 60 * index)).fadeIn(duration: BlindifyMotion.normal).slideY(
                      begin: 0.25,
                      curve: BlindifyMotion.pop,
                    );
              },
            ),
          ),
        ],
      ),
    );
  }
}
