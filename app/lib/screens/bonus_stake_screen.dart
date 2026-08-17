import 'dart:async';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/game_connection.dart';
import '../theme.dart';
import '../widgets/game_card.dart';

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
    _remainingMs = game.bonusStakeOptions?.dureePhaseMiseMs ?? 0;
    _ticker = Timer.periodic(const Duration(milliseconds: 100), (_) {
      setState(() => _remainingMs = (_remainingMs - 100).clamp(0, _remainingMs));
    });
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

    final disabled = game.bonusStakeEnvoyee || game.paused;

    return GameCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.casino_rounded, color: BlindifyColors.accent2),
              const SizedBox(width: 8),
              Expanded(child: Text('Mise à l\'aveugle', style: Theme.of(context).textTheme.headlineSmall)),
            ],
          ),
          const SizedBox(height: 8),
          Text(
            'Choisis un palier avant de découvrir la question. Pas de choix dans le délai = palier "safe" appliqué automatiquement.',
            style: Theme.of(context).textTheme.bodyMedium,
          ),
          const SizedBox(height: 4),
          Text('${(_remainingMs / 1000).ceil()}s restantes', style: Theme.of(context).textTheme.bodySmall),
          const SizedBox(height: 16),
          if (game.paused) const _Banner(text: 'Partie en pause — en attente du host.', color: BlindifyColors.warn),
          if (game.bonusStakeEnvoyee && !game.paused)
            const _Banner(text: 'Mise envoyée — en attente des autres joueurs.', color: BlindifyColors.good),
          const SizedBox(height: 12),
          Expanded(
            child: ListView.separated(
              itemCount: options.paliers.length,
              separatorBuilder: (context, index) => const SizedBox(height: 12),
              itemBuilder: (context, index) {
                final valeur = options.paliers[index];
                final selectionne = game.bonusPalierSelectionne == index;
                return Material(
                  color: selectionne ? BlindifyColors.accent : BlindifyColors.surfaceAlt,
                  borderRadius: BorderRadius.circular(14),
                  child: InkWell(
                    borderRadius: BorderRadius.circular(14),
                    onTap: disabled ? null : () => context.read<GameConnection>().selectStake(index),
                    child: Container(
                      padding: const EdgeInsets.all(16),
                      decoration: BoxDecoration(
                        borderRadius: BorderRadius.circular(14),
                        border: Border.all(color: selectionne ? BlindifyColors.accent : BlindifyColors.border),
                      ),
                      child: Text(
                        'Palier ${index + 1}${index == 0 ? " (safe)" : ""} — $valeur pts',
                        textAlign: TextAlign.center,
                        style: TextStyle(
                          fontWeight: FontWeight.w700,
                          color: selectionne ? BlindifyColors.onAccent : BlindifyColors.text,
                        ),
                      ),
                    ),
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

class _Banner extends StatelessWidget {
  const _Banner({required this.text, required this.color});

  final String text;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.15),
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: color.withValues(alpha: 0.4)),
      ),
      child: Text(text, style: TextStyle(color: color, fontWeight: FontWeight.w600)),
    );
  }
}
