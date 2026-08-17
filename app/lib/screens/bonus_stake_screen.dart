import 'dart:async';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/game_connection.dart';

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

    return Padding(
      padding: const EdgeInsets.all(24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Question bonus — mise à l\'aveugle', style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 8),
          const Text('Choisis un palier avant de découvrir la question. Pas de choix dans le délai = palier "safe" appliqué automatiquement.'),
          const SizedBox(height: 4),
          Text('${(_remainingMs / 1000).ceil()}s restantes', style: Theme.of(context).textTheme.bodyMedium),
          const SizedBox(height: 16),
          if (game.paused) const _Banner(text: 'Partie en pause — en attente du host.'),
          if (game.bonusStakeEnvoyee && !game.paused) const _Banner(text: 'Mise envoyée — en attente des autres joueurs.'),
          const SizedBox(height: 12),
          Expanded(
            child: ListView.separated(
              itemCount: options.paliers.length,
              separatorBuilder: (context, index) => const SizedBox(height: 12),
              itemBuilder: (context, index) {
                final valeur = options.paliers[index];
                final selectionne = game.bonusPalierSelectionne == index;
                return FilledButton.tonal(
                  onPressed: disabled ? null : () => context.read<GameConnection>().selectStake(index),
                  style: FilledButton.styleFrom(
                    padding: const EdgeInsets.all(16),
                    backgroundColor: selectionne ? Theme.of(context).colorScheme.primary : null,
                    foregroundColor: selectionne ? Theme.of(context).colorScheme.onPrimary : null,
                  ),
                  child: Text(
                    'Palier ${index + 1}${index == 0 ? " (safe)" : ""} — $valeur pts',
                    textAlign: TextAlign.center,
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
  const _Banner({required this.text});

  final String text;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(color: Colors.orange.shade100, borderRadius: BorderRadius.circular(8)),
      child: Text(text),
    );
  }
}
