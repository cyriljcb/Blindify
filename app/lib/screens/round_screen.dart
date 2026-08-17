import 'dart:async';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../models/round_cible.dart';
import '../models/round_mode.dart';
import '../models/round_started.dart';
import '../services/game_connection.dart';
import '../theme.dart';
import '../widgets/cover_art.dart';
import '../widgets/game_card.dart';

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

    return GameCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(round.mode.label, style: Theme.of(context).textTheme.headlineSmall),
                    const SizedBox(height: 2),
                    Text('Trouve ${round.cible.label} du morceau', style: Theme.of(context).textTheme.bodyMedium),
                  ],
                ),
              ),
              const SizedBox(width: 16),
              const MysteryCoverArt(size: 64),
            ],
          ),
          const SizedBox(height: 16),
          _RoundTimerBar(progress: progress),
          const SizedBox(height: 4),
          Text('${(_remainingMs / 1000).ceil()}s restantes', style: Theme.of(context).textTheme.bodySmall),
          const SizedBox(height: 16),
          if (game.paused) const _Banner(text: 'Partie en pause — en attente du host.', color: BlindifyColors.warn),
          if (game.roundAnswered && !game.paused)
            const _Banner(text: 'Réponse envoyée — en attente des autres joueurs.', color: BlindifyColors.good),
          const SizedBox(height: 8),
          Expanded(
            child: switch (round.mode) {
              RoundMode.qcm => _QcmAnswers(round: round, disabled: disabled),
              RoundMode.premiereLettre => _LetterAnswer(disabled: disabled),
              RoundMode.tapeReponse => _TextAnswer(controller: _reponseController, disabled: disabled, cible: round.cible),
            },
          ),
        ],
      ),
    );
  }
}

class _RoundTimerBar extends StatelessWidget {
  const _RoundTimerBar({required this.progress});

  final double progress;

  @override
  Widget build(BuildContext context) {
    final pct = progress.clamp(0, 1) * 100;
    final color = pct <= 15 ? BlindifyColors.bad : (pct <= 40 ? BlindifyColors.warn : BlindifyColors.accent);

    return ClipRRect(
      borderRadius: BorderRadius.circular(999),
      child: TweenAnimationBuilder<Color?>(
        tween: ColorTween(end: color),
        duration: const Duration(milliseconds: 300),
        builder: (context, animatedColor, _) => LinearProgressIndicator(
          value: progress.clamp(0, 1),
          minHeight: 10,
          backgroundColor: BlindifyColors.surfaceAlt,
          valueColor: AlwaysStoppedAnimation(animatedColor ?? BlindifyColors.accent),
        ),
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
      margin: const EdgeInsets.only(bottom: 8),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.15),
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: color.withValues(alpha: 0.4)),
      ),
      child: Text(text, style: TextStyle(color: color, fontWeight: FontWeight.w600)),
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
        // Un seul champ affiché par option (titre OU premier auteur), pas les deux — un
        // morceau à plusieurs auteurs listés en entier rend le QCM illisible.
        final label = round.cible == RoundCible.titre ? option.title : option.artist.split(',').first.trim();
        return _AnswerTile(
          label: label,
          onPressed: disabled ? null : () => context.read<GameConnection>().submitAnswer(option.trackId),
        );
      },
    );
  }
}

class _AnswerTile extends StatelessWidget {
  const _AnswerTile({required this.label, required this.onPressed});

  final String label;
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: BlindifyColors.surfaceAlt,
      borderRadius: BorderRadius.circular(14),
      child: InkWell(
        borderRadius: BorderRadius.circular(14),
        onTap: onPressed,
        child: Container(
          width: double.infinity,
          padding: const EdgeInsets.symmetric(vertical: 18, horizontal: 16),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(14),
            border: Border.all(color: BlindifyColors.border),
          ),
          child: Text(
            label,
            textAlign: TextAlign.center,
            style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 16),
          ),
        ),
      ),
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
        return Material(
          color: BlindifyColors.surfaceAlt,
          borderRadius: BorderRadius.circular(10),
          child: InkWell(
            borderRadius: BorderRadius.circular(10),
            onTap: disabled ? null : () => context.read<GameConnection>().submitAnswer(letter),
            child: Container(
              decoration: BoxDecoration(
                borderRadius: BorderRadius.circular(10),
                border: Border.all(color: BlindifyColors.border),
              ),
              alignment: Alignment.center,
              child: Text(letter, style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w800)),
            ),
          ),
        );
      },
    );
  }
}

class _TextAnswer extends StatelessWidget {
  const _TextAnswer({required this.controller, required this.disabled, required this.cible});

  final TextEditingController controller;
  final bool disabled;
  final RoundCible cible;

  @override
  Widget build(BuildContext context) {
    final label = cible == RoundCible.titre ? 'Titre du morceau' : "Nom de l'artiste";
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        TextField(
          controller: controller,
          enabled: !disabled,
          decoration: InputDecoration(labelText: label),
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
