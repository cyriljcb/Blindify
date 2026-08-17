import 'dart:async';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/game_connection.dart';

class BonusQuestionScreen extends StatefulWidget {
  const BonusQuestionScreen({super.key});

  @override
  State<BonusQuestionScreen> createState() => _BonusQuestionScreenState();
}

class _BonusQuestionScreenState extends State<BonusQuestionScreen> {
  final _reponseController = TextEditingController();
  Timer? _ticker;
  int _remainingMs = 0;

  @override
  void initState() {
    super.initState();
    final game = context.read<GameConnection>();
    _remainingMs = game.bonusQuestion?.dureePhaseQuestionMs ?? 0;
    _ticker = Timer.periodic(const Duration(milliseconds: 100), (_) {
      setState(() => _remainingMs = (_remainingMs - 100).clamp(0, _remainingMs));
    });
  }

  @override
  void dispose() {
    _ticker?.cancel();
    _reponseController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();

    if (game.bonusQuestion == null) {
      return const Center(child: Text('En attente de la question...'));
    }

    final disabled = game.bonusAnswered || game.paused;

    return Padding(
      padding: const EdgeInsets.all(24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Question bonus', style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 8),
          const Text('Le morceau (ralenti) est joué côté host — écoute et tape le titre. Un seul essai.'),
          const SizedBox(height: 4),
          Text('${(_remainingMs / 1000).ceil()}s restantes', style: Theme.of(context).textTheme.bodyMedium),
          const SizedBox(height: 16),
          if (game.paused) const _Banner(text: 'Partie en pause — en attente du host.'),
          if (game.bonusAnswered && !game.paused) const _Banner(text: 'Réponse envoyée — en attente des autres joueurs.'),
          const Spacer(),
          TextField(
            controller: _reponseController,
            enabled: !disabled,
            decoration: const InputDecoration(labelText: 'Titre du morceau', border: OutlineInputBorder()),
            onSubmitted: disabled ? null : (value) => context.read<GameConnection>().submitBonusAnswer(value.trim()),
          ),
          const SizedBox(height: 12),
          FilledButton(
            onPressed: disabled ? null : () => context.read<GameConnection>().submitBonusAnswer(_reponseController.text.trim()),
            child: const Text('Valider'),
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
      margin: const EdgeInsets.only(bottom: 8),
      decoration: BoxDecoration(color: Colors.orange.shade100, borderRadius: BorderRadius.circular(8)),
      child: Text(text),
    );
  }
}
