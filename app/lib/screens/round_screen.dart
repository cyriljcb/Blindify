import 'dart:async';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../models/round_mode.dart';
import '../models/round_started.dart';
import '../services/game_connection.dart';

class RoundScreen extends StatefulWidget {
  const RoundScreen({super.key});

  @override
  State<RoundScreen> createState() => _RoundScreenState();
}

class _RoundScreenState extends State<RoundScreen> {
  final _reponseController = TextEditingController();
  Timer? _ticker;
  int _remainingMs = 0;

  @override
  void initState() {
    super.initState();
    final game = context.read<GameConnection>();
    _remainingMs = game.currentRound?.dureeFenetreReponseMs ?? 0;
    if (!game.paused) _startTicker();
  }

  void _startTicker() {
    _ticker?.cancel();
    _ticker = Timer.periodic(const Duration(milliseconds: 100), (_) {
      setState(() => _remainingMs = (_remainingMs - 100).clamp(0, _remainingMs));
      if (_remainingMs <= 0) _ticker?.cancel();
    });
  }

  // Idempotent : ne (re)démarre/n'arrête le ticker que si l'état de pause a réellement
  // changé, en miroir de pauseTimer()/resumeTimer() côté host (host/app.js:129-140) —
  // le serveur reste seul juge du timing réel, ce décompte n'est qu'indicatif.
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
    _reponseController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();
    final round = game.currentRound;

    if (round == null) {
      return const Center(child: Text('En attente du round...'));
    }

    _syncTickerWithPause(game.paused);

    final disabled = game.roundAnswered || game.paused;
    final totalMs = round.dureeFenetreReponseMs;
    final progress = totalMs > 0 ? _remainingMs / totalMs : 0.0;

    return Padding(
      padding: const EdgeInsets.all(24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Round en cours — ${round.mode.label}', style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 12),
          ClipRRect(
            borderRadius: BorderRadius.circular(999),
            child: LinearProgressIndicator(value: progress, minHeight: 10),
          ),
          const SizedBox(height: 4),
          Text('${(_remainingMs / 1000).ceil()}s restantes', style: Theme.of(context).textTheme.bodyMedium),
          const SizedBox(height: 16),
          if (game.paused) const _Banner(text: 'Partie en pause — en attente du host.', color: Colors.orange),
          if (game.roundAnswered && !game.paused)
            const _Banner(text: 'Réponse envoyée — en attente des autres joueurs.', color: Colors.green),
          const SizedBox(height: 16),
          Expanded(
            child: switch (round.mode) {
              RoundMode.qcm => _QcmAnswers(round: round, disabled: disabled),
              RoundMode.premiereLettre => _LetterAnswer(disabled: disabled),
              RoundMode.tapeReponse => _TextAnswer(controller: _reponseController, disabled: disabled),
            },
          ),
        ],
      ),
    );
  }
}

class _Banner extends StatelessWidget {
  const _Banner({required this.text, required this.color});

  final String text;
  final MaterialColor color;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(12),
      margin: const EdgeInsets.only(bottom: 8),
      decoration: BoxDecoration(color: color.shade100, borderRadius: BorderRadius.circular(8)),
      child: Text(text),
    );
  }
}

class _QcmAnswers extends StatelessWidget {
  const _QcmAnswers({required this.round, required this.disabled});

  final RoundStarted round;
  final bool disabled;

  @override
  Widget build(BuildContext context) {
    final options = round.qcmOptions ?? [];

    return ListView.separated(
      itemCount: options.length,
      separatorBuilder: (context, index) => const SizedBox(height: 12),
      itemBuilder: (context, index) {
        final option = options[index];
        return FilledButton.tonal(
          onPressed: disabled ? null : () => context.read<GameConnection>().submitAnswer(option.trackId),
          style: FilledButton.styleFrom(padding: const EdgeInsets.all(16)),
          child: Text('${option.title} — ${option.artist}', textAlign: TextAlign.center),
        );
      },
    );
  }
}

class _LetterAnswer extends StatelessWidget {
  const _LetterAnswer({required this.disabled});

  final bool disabled;

  static const _letters = [
    'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M',
    'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
  ];

  @override
  Widget build(BuildContext context) {
    return GridView.builder(
      gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
        crossAxisCount: 6,
        mainAxisSpacing: 8,
        crossAxisSpacing: 8,
      ),
      itemCount: _letters.length,
      itemBuilder: (context, index) {
        final letter = _letters[index];
        return FilledButton.tonal(
          onPressed: disabled ? null : () => context.read<GameConnection>().submitAnswer(letter),
          style: FilledButton.styleFrom(padding: EdgeInsets.zero),
          child: Text(letter, style: const TextStyle(fontSize: 20, fontWeight: FontWeight.bold)),
        );
      },
    );
  }
}

class _TextAnswer extends StatelessWidget {
  const _TextAnswer({required this.controller, required this.disabled});

  final TextEditingController controller;
  final bool disabled;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        TextField(
          controller: controller,
          enabled: !disabled,
          decoration: const InputDecoration(labelText: 'Ta réponse', border: OutlineInputBorder()),
          onSubmitted: disabled ? null : (value) => context.read<GameConnection>().submitAnswer(value.trim()),
        ),
        const SizedBox(height: 12),
        FilledButton(
          onPressed: disabled ? null : () => context.read<GameConnection>().submitAnswer(controller.text.trim()),
          child: const Text('Valider'),
        ),
      ],
    );
  }
}
