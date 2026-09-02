import 'dart:async';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../models/qcm_option.dart';
import '../models/round_cible.dart';
import '../models/round_mode.dart';
import '../services/game_connection.dart';
import '../theme.dart';
import '../widgets/answer_banner.dart';
import '../widgets/cover_art.dart';
import '../widgets/fill_height_list.dart';
import '../widgets/game_card.dart';
import '../widgets/serie_badge.dart';
import '../widgets/timer_bar.dart';

enum AnswerPhaseVariant { classique, bonus }

/// Écran de réponse générique — fusionne round_screen.dart et bonus_question_screen.dart
/// (docs/refactor-decisions.md section 4, ~640 lignes dupliquées) : même ticker 100ms, même
/// auto-submit d'une saisie texte en fin de délai, mêmes 3 sous-widgets de réponse
/// (_QcmAnswers/_LetterAnswer/_TextAnswer) et la même bannière (AnswerBanner, partagée aussi avec
/// bonus_stake_screen.dart). Seuls l'en-tête et la bannière "course" (bonus uniquement) diffèrent
/// selon [variant] — trop différents pour un layout unique paramétré, donc deux méthodes de build
/// séparées qui ne dupliquent que la structure `GameCard`, pas la logique.
class AnswerPhaseScreen extends StatefulWidget {
  const AnswerPhaseScreen({super.key, required this.variant});

  final AnswerPhaseVariant variant;

  @override
  State<AnswerPhaseScreen> createState() => _AnswerPhaseScreenState();
}

class _AnswerPhaseScreenState extends State<AnswerPhaseScreen> {
  final _reponseController = TextEditingController();
  Timer? _ticker;
  int _remainingMs = 0;
  bool _autoSubmitDeclenche = false;

  bool get _bonus => widget.variant == AnswerPhaseVariant.bonus;

  @override
  void initState() {
    super.initState();
    final game = context.read<GameConnection>();
    _remainingMs = _bonus
        ? (game.bonusQuestion?.dureePhaseQuestionMs ?? 0)
        : (game.currentRound?.dureeFenetreReponseMs ?? 0);
    if (!game.paused) _startTicker();
  }

  // Marge avant zéro à laquelle la validation auto est tentée, pas au tout dernier tick — sans
  // cette marge, le décompte visuel continue jusqu'à 0 normalement. Particulièrement utile côté
  // bonus : le timer serveur (BonusTimerCoordinator, vérifié toutes les 250ms) détruit cet écran
  // dès l'échéance, attendre pile 0 perdait quasi systématiquement la course contre ce timeout.
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

  // Timer écoulé : valide automatiquement une saisie texte déjà tapée plutôt que de la perdre
  // silencieusement. Ne concerne que le mode "tape la réponse" — QCM/lettre n'ont pas de saisie
  // libre à récupérer, un timer écoulé sans clic y reste une absence de réponse assumée.
  void _autoSubmitSiSaisie() {
    final game = context.read<GameConnection>();
    final mode = _bonus ? game.bonusQuestion?.mode : game.currentRound?.mode;
    final dejaRepondu = _bonus ? game.bonusAnswered : game.roundAnswered;
    if (dejaRepondu || mode != RoundMode.tapeReponse) return;
    final reponse = _reponseController.text.trim();
    if (reponse.isEmpty) return;
    if (_bonus) {
      game.submitBonusAnswer(reponse);
    } else {
      game.submitAnswer(reponse);
    }
  }

  // Idempotent : ne (re)démarre/n'arrête le ticker que si l'état de pause a réellement changé, en
  // miroir de pauseTimer()/resumeTimer() côté host — le serveur reste seul juge du timing réel.
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
    return _bonus ? _buildBonus(context, game) : _buildClassique(context, game);
  }

  Widget _buildClassique(BuildContext context, GameConnection game) {
    final round = game.currentRound;
    if (round == null) {
      return const Center(child: Text('En attente du round...'));
    }

    _syncTickerWithPause(game.paused);
    final disabled = game.roundAnswered || game.paused;
    final progress = round.dureeFenetreReponseMs > 0 ? _remainingMs / round.dureeFenetreReponseMs : 0.0;

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
                    const SizedBox(height: 6),
                    SerieBadge(serieIndex: round.serieIndex),
                  ],
                ),
              ),
              const SizedBox(width: 16),
              const MysteryCoverArt(size: 64),
            ],
          ),
          const SizedBox(height: 16),
          TimerBar(progress: progress, secondesRestantes: (_remainingMs / 1000).ceil()),
          const SizedBox(height: 16),
          if (game.paused) const AnswerBanner(text: 'Partie en pause — en attente du host.', color: BlindifyColors.warn),
          if (game.roundAnswered && !game.paused)
            const AnswerBanner(text: 'Réponse envoyée — en attente des autres joueurs.', color: BlindifyColors.good),
          const SizedBox(height: 8),
          Expanded(
            child: _buildAnswerArea(
              mode: round.mode,
              cible: round.cible,
              qcmOptions: round.qcmOptions,
              disabled: disabled,
              onSubmit: game.submitAnswer,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildBonus(BuildContext context, GameConnection game) {
    final question = game.bonusQuestion;
    if (question == null) {
      return const Center(child: Text('En attente de la question...'));
    }

    _syncTickerWithPause(game.paused);
    final disabled = game.bonusAnswered || game.paused;
    final progress = question.dureePhaseQuestionMs > 0 ? _remainingMs / question.dureePhaseQuestionMs : 0.0;

    return GameCard(
      // Bascule tout le contour/l'ombre en mustard pendant la course — l'écran d'annonce
      // (BonusCourseIntroScreen) a déjà expliqué la règle en détail juste avant, inutile de la
      // répéter en pavé ici (retour utilisateur : ça écrasait les boutons de réponse).
      accentColor: question.estCourse ? BlindifyColors.mustard : null,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Text('Question bonus', style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 6),
          Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              SerieBadge(serieIndex: question.serieIndex),
              if (question.estCourse) ...[const SizedBox(width: 8), const _CourseBadge()],
            ],
          ),
          const SizedBox(height: 12),
          const MysteryCoverArt(size: 110),
          const SizedBox(height: 12),
          Text(
            switch (question.cible) {
              RoundCible.film => 'Le morceau (ralenti) est joué côté host — écoute et trouve le film. Un seul essai.',
              RoundCible.auteur =>
                "Le morceau (ralenti) est joué côté host — écoute et trouve l'artiste. Un seul essai.",
              RoundCible.titre => 'Le morceau (ralenti) est joué côté host — écoute et tape le titre. Un seul essai.',
            },
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.bodyMedium,
          ),
          const SizedBox(height: 12),
          TimerBar(progress: progress, secondesRestantes: (_remainingMs / 1000).ceil()),
          const SizedBox(height: 16),
          if (game.paused) const AnswerBanner(text: 'Partie en pause — en attente du host.', color: BlindifyColors.warn),
          if (game.bonusAnswered && !game.paused)
            const AnswerBanner(text: 'Réponse envoyée — en attente des autres joueurs.', color: BlindifyColors.good),
          const SizedBox(height: 8),
          Expanded(
            child: _buildAnswerArea(
              mode: question.mode,
              cible: question.cible,
              qcmOptions: question.qcmOptions,
              disabled: disabled,
              onSubmit: game.submitBonusAnswer,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildAnswerArea({
    required RoundMode mode,
    required RoundCible cible,
    required List<QcmOption>? qcmOptions,
    required bool disabled,
    required Future<void> Function(String) onSubmit,
  }) {
    return switch (mode) {
      RoundMode.qcm => _QcmAnswers(qcmOptions: qcmOptions ?? [], cible: cible, disabled: disabled, onSubmit: onSubmit),
      RoundMode.premiereLettre => _LetterAnswer(disabled: disabled, onSubmit: onSubmit),
      RoundMode.tapeReponse =>
        _TextAnswer(controller: _reponseController, disabled: disabled, cible: cible, onSubmit: onSubmit),
    };
  }
}

/// Rappel discret du mode course sur l'écran de question — le détail des règles reste sur
/// BonusCourseIntroScreen, affiché juste avant (voir _buildBonus ci-dessus).
class _CourseBadge extends StatelessWidget {
  const _CourseBadge();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(
        color: BlindifyColors.mustard.withValues(alpha: 0.18),
        border: Border.all(color: BlindifyColors.mustard, width: 2),
        borderRadius: BorderRadius.circular(999),
      ),
      child: const Text(
        '🏁 Course',
        style: TextStyle(fontWeight: FontWeight.w700, fontSize: 12, color: BlindifyColors.mustard),
      ),
    );
  }
}

class _QcmAnswers extends StatelessWidget {
  const _QcmAnswers({required this.qcmOptions, required this.cible, required this.disabled, required this.onSubmit});

  final List<QcmOption> qcmOptions;
  final RoundCible cible;
  final bool disabled;
  final Future<void> Function(String) onSubmit;

  @override
  Widget build(BuildContext context) {
    return FillHeightList(
      itemCount: qcmOptions.length,
      itemBuilder: (context, index) {
        final option = qcmOptions[index];
        // Un seul champ affiché par option (titre, premier auteur, ou film), pas plusieurs — un
        // morceau à plusieurs auteurs listés en entier rend le QCM illisible.
        final label = switch (cible) {
          RoundCible.titre => option.title,
          RoundCible.auteur => option.artist.split(',').first.trim(),
          RoundCible.film => option.film,
        };
        return _AnswerTile(label: label, onPressed: disabled ? null : () => onSubmit(option.trackId));
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
          alignment: Alignment.center,
          // Centré plutôt qu'un padding vertical fixe (18) : la tuile a désormais une hauteur
          // calculée par _QcmAnswers pour remplir l'espace disponible, parfois plus serrée qu'avant
          // — un padding fixe y déborderait, alors qu'un centrage s'adapte à toute hauteur.
          padding: const EdgeInsets.symmetric(horizontal: 16),
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
  const _LetterAnswer({required this.disabled, required this.onSubmit});

  final bool disabled;
  final Future<void> Function(String) onSubmit;

  static const _letters = [
    'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M',
    'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
  ];

  static const _spacing = 10.0;

  // Plage de colonnes testée pour choisir le nombre le plus proche d'une tuile carrée — pas de
  // valeur fixe à 5 (retour utilisateur : les lettres paraissaient "écrasées", tuiles bien plus
  // larges que hautes, dès que l'espace vertical disponible se réduisait, ex. avec l'en-tête et
  // le timer au-dessus). En dessous de 4, les tuiles deviennent trop grandes pour rester carrées
  // sur un écran étroit ; au-dessus de 8, elles deviennent trop petites pour rester tapables.
  static const _minColumns = 4;
  static const _maxColumns = 8;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final crossAxisCount = _bestColumnCount(constraints.maxWidth, constraints.maxHeight);
        final rows = (_letters.length / crossAxisCount).ceil();
        final tileWidth = (constraints.maxWidth - _spacing * (crossAxisCount - 1)) / crossAxisCount;
        final tileHeight = (constraints.maxHeight - _spacing * (rows - 1)) / rows;

        return GridView.builder(
          gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
            crossAxisCount: crossAxisCount,
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
                onTap: disabled ? null : () => onSubmit(letter),
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

  // Teste chaque nombre de colonnes de la plage et garde celui qui donne le ratio largeur/hauteur
  // de tuile le plus proche de 1 (carré), pour l'espace effectivement disponible.
  static int _bestColumnCount(double maxWidth, double maxHeight) {
    var best = _minColumns;
    var bestDeviation = double.infinity;
    for (var columns = _minColumns; columns <= _maxColumns; columns++) {
      final rows = (_letters.length / columns).ceil();
      final tileWidth = (maxWidth - _spacing * (columns - 1)) / columns;
      final tileHeight = (maxHeight - _spacing * (rows - 1)) / rows;
      if (tileWidth <= 0 || tileHeight <= 0) continue;
      final deviation = (tileWidth / tileHeight - 1).abs();
      if (deviation < bestDeviation) {
        bestDeviation = deviation;
        best = columns;
      }
    }
    return best;
  }
}

class _TextAnswer extends StatelessWidget {
  const _TextAnswer({required this.controller, required this.disabled, required this.cible, required this.onSubmit});

  final TextEditingController controller;
  final bool disabled;
  final RoundCible cible;
  final Future<void> Function(String) onSubmit;

  @override
  Widget build(BuildContext context) {
    final label = switch (cible) {
      RoundCible.titre => 'Titre du morceau',
      RoundCible.auteur => "Nom de l'artiste",
      RoundCible.film => 'Film Disney',
    };
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        TextField(
          controller: controller,
          enabled: !disabled,
          decoration: InputDecoration(labelText: label),
          onSubmitted: disabled ? null : (value) => onSubmit(value.trim()),
        ),
        const SizedBox(height: 12),
        FilledButton(
          onPressed: disabled ? null : () => onSubmit(controller.text.trim()),
          child: const Text('Valider'),
        ),
      ],
    );
  }
}
