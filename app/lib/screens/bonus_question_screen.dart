import 'dart:async';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../models/bonus_question_started.dart';
import '../models/round_cible.dart';
import '../models/round_mode.dart';
import '../services/game_connection.dart';
import '../theme.dart';
import '../widgets/cover_art.dart';
import '../widgets/game_card.dart';
import '../widgets/serie_badge.dart';
import '../widgets/timer_bar.dart';

class BonusQuestionScreen extends StatefulWidget {
  const BonusQuestionScreen({super.key});

  @override
  State<BonusQuestionScreen> createState() => _BonusQuestionScreenState();
}

class _BonusQuestionScreenState extends State<BonusQuestionScreen> {
  final _reponseController = TextEditingController();
  Timer? _ticker;
  int _remainingMs = 0;
  bool _autoSubmitDeclenche = false;

  @override
  void initState() {
    super.initState();
    final game = context.read<GameConnection>();
    _remainingMs = game.bonusQuestion?.dureePhaseQuestionMs ?? 0;
    if (!game.paused) _startTicker();
  }

  // Marge avant zéro (voir _autoSubmitSiSaisie) à laquelle la validation auto est tentée, pas
  // au tout dernier tick — sans cette marge, le décompte visuel continue jusqu'à 0 normalement.
  static const _margeAutoSubmitMs = 1000;

  void _startTicker() {
    _ticker?.cancel();
    _ticker = Timer.periodic(const Duration(milliseconds: 100), (_) {
      setState(() => _remainingMs = (_remainingMs - 100).clamp(0, _remainingMs));
      if (_remainingMs <= _margeAutoSubmitMs && !_autoSubmitDeclenche) {
        _autoSubmitDeclenche = true;
        _autoSubmitSiSaisie();
      }
      if (_remainingMs <= 0) _ticker?.cancel();
    });
  }

  // Timer écoulé : valide automatiquement une saisie déjà tapée — voir RoundScreen, encore plus
  // pertinent ici puisqu'il n'y a aucun enjeu de rapidité sur la question bonus (pas de
  // dégressivité), seulement le risque de perdre une réponse déjà tapée faute de clic à temps.
  // Déclenché avec une marge avant zéro (_margeAutoSubmitMs) plutôt que pile à 0 : le décompte
  // local démarre au rendu du widget, donc déjà légèrement en retard sur l'horloge serveur qui
  // fait foi pour la vraie fin de phase (BonusTimerCoordinator, vérifiée côté serveur toutes les
  // 250ms) — attendre pile 0 perdait quasi systématiquement la course contre le timeout serveur,
  // qui détruit cet écran (et son ticker) avant que la validation locale ait pu partir.
  void _autoSubmitSiSaisie() {
    final game = context.read<GameConnection>();
    // Mode tiré aléatoirement depuis le retour utilisateur du 2026-08-27 (QCM/Première lettre en
    // plus de la réponse tapée, comme un round classique) — ne concerne que TapeReponse, voir
    // RoundScreen._autoSubmitSiSaisie pour le même raisonnement.
    if (game.bonusAnswered || game.bonusQuestion?.mode != RoundMode.tapeReponse) return;
    final reponse = _reponseController.text.trim();
    if (reponse.isNotEmpty) game.submitBonusAnswer(reponse);
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
    _reponseController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();

    if (game.bonusQuestion == null) {
      return const Center(child: Text('En attente de la question...'));
    }

    _syncTickerWithPause(game.paused);

    final disabled = game.bonusAnswered || game.paused;
    final cible = game.bonusQuestion!.cible;
    final totalMs = game.bonusQuestion!.dureePhaseQuestionMs;
    final progress = totalMs > 0 ? _remainingMs / totalMs : 0.0;

    return GameCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Text('Question bonus', style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 6),
          SerieBadge(serieIndex: game.bonusQuestion!.serieIndex),
          const SizedBox(height: 12),
          const MysteryCoverArt(size: 110),
          const SizedBox(height: 12),
          Text(
            switch (cible) {
              RoundCible.film => 'Le morceau (ralenti) est joué côté host — écoute et trouve le film. Un seul essai.',
              RoundCible.auteur => "Le morceau (ralenti) est joué côté host — écoute et trouve l'artiste. Un seul essai.",
              RoundCible.titre => 'Le morceau (ralenti) est joué côté host — écoute et tape le titre. Un seul essai.',
            },
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.bodyMedium,
          ),
          const SizedBox(height: 12),
          TimerBar(progress: progress, secondesRestantes: (_remainingMs / 1000).ceil()),
          const SizedBox(height: 16),
          if (game.paused) const _Banner(text: 'Partie en pause — en attente du host.', color: BlindifyColors.warn),
          if (game.bonusAnswered && !game.paused)
            const _Banner(text: 'Réponse envoyée — en attente des autres joueurs.', color: BlindifyColors.good),
          const SizedBox(height: 8),
          Expanded(
            child: switch (game.bonusQuestion!.mode) {
              RoundMode.qcm => _QcmAnswers(question: game.bonusQuestion!, disabled: disabled),
              RoundMode.premiereLettre => _LetterAnswer(disabled: disabled),
              RoundMode.tapeReponse => _TextAnswer(controller: _reponseController, disabled: disabled, cible: cible),
            },
          ),
        ],
      ),
    );
  }
}

class _QcmAnswers extends StatelessWidget {
  const _QcmAnswers({required this.question, required this.disabled});

  final BonusQuestionStarted question;
  final bool disabled;

  @override
  Widget build(BuildContext context) {
    final options = question.qcmOptions ?? [];

    return ListView.separated(
      itemCount: options.length,
      separatorBuilder: (context, index) => const SizedBox(height: 12),
      itemBuilder: (context, index) {
        final option = options[index];
        // Un seul champ affiché par option, comme RoundScreen._QcmAnswers.
        final label = switch (question.cible) {
          RoundCible.titre => option.title,
          RoundCible.auteur => option.artist.split(',').first.trim(),
          RoundCible.film => option.film,
        };
        return _AnswerTile(
          label: label,
          onPressed: disabled ? null : () => context.read<GameConnection>().submitBonusAnswer(option.trackId),
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
      borderRadius: BorderRadius.circular(8),
      child: InkWell(
        borderRadius: BorderRadius.circular(8),
        onTap: onPressed,
        child: Container(
          width: double.infinity,
          padding: const EdgeInsets.symmetric(vertical: 18, horizontal: 16),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(8),
            border: Border.all(color: BlindifyColors.ink, width: 2),
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

  static const _crossAxisCount = 5;
  static const _spacing = 10.0;

  @override
  Widget build(BuildContext context) {
    // Même calcul que RoundScreen._LetterAnswer (retour utilisateur : occuper tout l'espace
    // vertical disponible plutôt qu'un ratio fixe qui laisse un reste).
    return LayoutBuilder(
      builder: (context, constraints) {
        final rows = (_letters.length / _crossAxisCount).ceil();
        final tileWidth = (constraints.maxWidth - _spacing * (_crossAxisCount - 1)) / _crossAxisCount;
        final tileHeight = (constraints.maxHeight - _spacing * (rows - 1)) / rows;

        return GridView.builder(
          gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
            crossAxisCount: _crossAxisCount,
            mainAxisSpacing: _spacing,
            crossAxisSpacing: _spacing,
            childAspectRatio: tileWidth / tileHeight,
          ),
          itemCount: _letters.length,
          itemBuilder: (context, index) {
            final letter = _letters[index];
            return Material(
              color: BlindifyColors.surfaceAlt,
              borderRadius: BorderRadius.circular(8),
              child: InkWell(
                borderRadius: BorderRadius.circular(8),
                onTap: disabled ? null : () => context.read<GameConnection>().submitBonusAnswer(letter),
                child: Container(
                  decoration: BoxDecoration(
                    borderRadius: BorderRadius.circular(8),
                    border: Border.all(color: BlindifyColors.ink, width: 2),
                  ),
                  alignment: Alignment.center,
                  child: Text(letter, style: const TextStyle(fontSize: 26, fontWeight: FontWeight.w800)),
                ),
              ),
            );
          },
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
    final label = switch (cible) {
      RoundCible.film => 'Film Disney',
      RoundCible.auteur => 'Artiste du morceau',
      RoundCible.titre => 'Titre du morceau',
    };
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        TextField(
          controller: controller,
          enabled: !disabled,
          decoration: InputDecoration(labelText: label),
          onSubmitted: disabled ? null : (value) => context.read<GameConnection>().submitBonusAnswer(value.trim()),
        ),
        const SizedBox(height: 12),
        FilledButton(
          onPressed: disabled ? null : () => context.read<GameConnection>().submitBonusAnswer(controller.text.trim()),
          child: const Text('Valider'),
        ),
      ],
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
