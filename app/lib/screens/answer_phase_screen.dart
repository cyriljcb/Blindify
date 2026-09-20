import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_animate/flutter_animate.dart';
import 'package:provider/provider.dart';

import '../models/joker_indice.dart';
import '../models/qcm_option.dart';
import '../models/round_cible.dart';
import '../models/round_mode.dart';
import '../motion.dart';
import '../services/game_connection.dart';
import '../theme.dart';
import '../widgets/answer_banner.dart';
import '../widgets/cover_art.dart';
import '../widgets/fill_height_list.dart';
import '../widgets/game_card.dart';
import '../widgets/joker_button.dart';
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
    // tempsEcouleMs > 0 quand cet écran est réaffiché après une reconnexion en pleine phase
    // (voir EtatCourantJoueur) — sans le soustraire, la barre de temps repartirait de la durée
    // totale au lieu du temps réellement restant.
    _remainingMs = _bonus
        ? (game.bonusQuestion == null
            ? 0
            : (game.bonusQuestion!.dureePhaseQuestionMs - game.bonusQuestion!.tempsEcouleMs)
                .clamp(0, game.bonusQuestion!.dureePhaseQuestionMs))
        : (game.currentRound == null
            ? 0
            : (game.currentRound!.dureeFenetreReponseMs - game.currentRound!.tempsEcouleMs)
                .clamp(0, game.currentRound!.dureeFenetreReponseMs));
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
          Row(
            children: [
              Expanded(child: TimerBar(progress: progress, secondesRestantes: (_remainingMs / 1000).ceil())),
              // Joker (V2, section 12.7) : jamais en bonus (cette méthode ne construit que l'écran
              // classique), masqué dès que le joueur a répondu — mais reste visible (grisé/barré)
              // une fois utilisé, ce n'est pas la même chose que masqué.
              if (!game.roundAnswered && !game.paused) ...[
                const SizedBox(width: 10),
                JokerButton(disponible: game.jokerDisponible, onActiver: game.utiliserJoker),
              ],
            ],
          ),
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
              anneeOptions: round.anneeOptions,
              disabled: disabled,
              onSubmit: game.submitAnswer,
              jokerIndice: game.jokerIndiceActuel,
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
              RoundCible.annee =>
                "Le morceau (ralenti) est joué côté host — écoute et trouve l'année de sortie. Un seul essai.",
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
              anneeOptions: question.anneeOptions,
              disabled: disabled,
              onSubmit: game.submitBonusAnswer,
            ),
          ),
        ],
      ),
    );
  }

  // Cible Année (V2, section 12.5) traitée à part de préférence au switch sur mode ci-dessous :
  // en Qcm les options sont des années (anneeOptions), pas des morceaux (qcmOptions) — jamais en
  // PremiereLettre (le serveur bascule alors en TapeReponse, voir RoundService.DemarrerRound).
  // jokerIndice (V2, section 12.7) : toujours null côté bonus (_buildBonus ne le passe pas).
  Widget _buildAnswerArea({
    required RoundMode mode,
    required RoundCible cible,
    required List<QcmOption>? qcmOptions,
    required List<String>? anneeOptions,
    required bool disabled,
    required Future<void> Function(String) onSubmit,
    JokerIndice? jokerIndice,
  }) {
    if (cible == RoundCible.annee) {
      return mode == RoundMode.qcm
          ? _AnneeQcmAnswers(anneeOptions: anneeOptions ?? [], disabled: disabled, onSubmit: onSubmit, optionsRetirees: jokerIndice?.optionsRetirees)
          : _AnneeInput(controller: _reponseController, disabled: disabled, onSubmit: onSubmit, decennie: jokerIndice?.decennie);
    }

    return switch (mode) {
      RoundMode.qcm => _QcmAnswers(qcmOptions: qcmOptions ?? [], cible: cible, disabled: disabled, onSubmit: onSubmit, optionsRetirees: jokerIndice?.optionsRetirees),
      RoundMode.premiereLettre => _LetterAnswer(disabled: disabled, onSubmit: onSubmit, tuilesRestantes: jokerIndice?.tuilesRestantes),
      RoundMode.tapeReponse => _TextAnswer(
          controller: _reponseController,
          disabled: disabled,
          cible: cible,
          onSubmit: onSubmit,
          structure: jokerIndice?.structure,
          coverUrl: context.read<GameConnection>().resolveServerUrl(jokerIndice?.coverUrl),
        ),
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
  const _QcmAnswers({required this.qcmOptions, required this.cible, required this.disabled, required this.onSubmit, this.optionsRetirees});

  final List<QcmOption> qcmOptions;
  final RoundCible cible;
  final bool disabled;
  final Future<void> Function(String) onSubmit;

  /// V2, section 12.7 — TrackId des options retirées par le joker (50/50), jamais la bonne réponse.
  final List<String>? optionsRetirees;

  @override
  Widget build(BuildContext context) {
    final optionsAffichees = optionsRetirees == null
        ? qcmOptions
        : qcmOptions.where((o) => !optionsRetirees!.contains(o.trackId)).toList();

    return FillHeightList(
      itemCount: optionsAffichees.length,
      itemBuilder: (context, index) {
        final option = optionsAffichees[index];
        // Un seul champ affiché par option (titre, premier auteur, ou film), pas plusieurs — un
        // morceau à plusieurs auteurs listés en entier rend le QCM illisible.
        final label = switch (cible) {
          RoundCible.titre => option.title,
          RoundCible.auteur => option.artist.split(',').first.trim(),
          RoundCible.film => option.film,
          // Jamais atteint en pratique (cible Année routée vers _AnneeQcmAnswers, voir
          // _buildAnswerArea) — présent pour l'exhaustivité du switch.
          RoundCible.annee => option.title,
        };
        return _AnswerTile(label: label, index: index, onPressed: disabled ? null : () => onSubmit(option.trackId));
      },
    );
  }
}

/// Écrasement au press (retour tactile façon appli de quiz "arcade") + entrée décalée par index à
/// l'apparition — retour utilisateur : les tuiles se contentaient d'un ripple Material, jugé mou.
class _AnswerTile extends StatefulWidget {
  const _AnswerTile({required this.label, required this.index, required this.onPressed});

  final String label;
  final int index;
  final VoidCallback? onPressed;

  @override
  State<_AnswerTile> createState() => _AnswerTileState();
}

class _AnswerTileState extends State<_AnswerTile> {
  bool _pressed = false;

  void _setPressed(bool value) {
    if (widget.onPressed == null) return;
    setState(() => _pressed = value);
  }

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTapDown: (_) => _setPressed(true),
      onTapUp: (_) => _setPressed(false),
      onTapCancel: () => _setPressed(false),
      onTap: widget.onPressed,
      child: AnimatedScale(
        scale: _pressed ? 0.94 : 1.0,
        duration: BlindifyMotion.fast,
        curve: Curves.easeOut,
        child: Container(
          width: double.infinity,
          alignment: Alignment.center,
          // Centré plutôt qu'un padding vertical fixe (18) : la tuile a désormais une hauteur
          // calculée par _QcmAnswers pour remplir l'espace disponible, parfois plus serrée qu'avant
          // — un padding fixe y déborderait, alors qu'un centrage s'adapte à toute hauteur.
          padding: const EdgeInsets.symmetric(horizontal: 16),
          decoration: BoxDecoration(
            color: BlindifyColors.surfaceAlt,
            borderRadius: BorderRadius.circular(8),
            border: Border.all(color: BlindifyColors.ink, width: 2),
          ),
          child: Text(
            widget.label,
            textAlign: TextAlign.center,
            style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 16),
          ),
        ),
      ),
    )
        .animate(delay: Duration(milliseconds: 60 * widget.index))
        .fadeIn(duration: BlindifyMotion.normal)
        .slideY(begin: 0.25, curve: BlindifyMotion.pop);
  }
}

class _LetterAnswer extends StatelessWidget {
  const _LetterAnswer({required this.disabled, required this.onSubmit, this.tuilesRestantes});

  final bool disabled;
  final Future<void> Function(String) onSubmit;

  /// V2, section 12.7 — si renseigné (joker utilisé), seules ces lettres restent affichées/
  /// sélectionnables (dont la bonne) à la place de l'alphabet complet.
  final List<String>? tuilesRestantes;

  static const _lettresCompletes = [
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
    final lettres = tuilesRestantes ?? _lettresCompletes;

    return LayoutBuilder(
      builder: (context, constraints) {
        final crossAxisCount = _bestColumnCount(lettres.length, constraints.maxWidth, constraints.maxHeight);
        final rows = (lettres.length / crossAxisCount).ceil();
        final tileWidth = (constraints.maxWidth - _spacing * (crossAxisCount - 1)) / crossAxisCount;
        final tileHeight = (constraints.maxHeight - _spacing * (rows - 1)) / rows;

        return GridView.builder(
          gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
            crossAxisCount: crossAxisCount,
            mainAxisSpacing: _spacing,
            crossAxisSpacing: _spacing,
            childAspectRatio: tileWidth / tileHeight,
          ),
          itemCount: lettres.length,
          itemBuilder: (context, index) {
            final letter = lettres[index];
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
        )
            // Grille entière plutôt qu'une tuile à la fois (26 lettres, un décalage par tuile
            // deviendrait trop lent à l'arrivée) — juste assez de mouvement pour éviter un simple
            // "pop" statique.
            .animate()
            .fadeIn(duration: BlindifyMotion.normal)
            .scale(begin: const Offset(0.97, 0.97), curve: BlindifyMotion.snappy);
      },
    );
  }

  // Teste chaque nombre de colonnes de la plage et garde celui qui donne le ratio largeur/hauteur
  // de tuile le plus proche de 1 (carré), pour l'espace effectivement disponible.
  static int _bestColumnCount(int letterCount, double maxWidth, double maxHeight) {
    var best = _minColumns;
    var bestDeviation = double.infinity;
    for (var columns = _minColumns; columns <= _maxColumns; columns++) {
      final rows = (letterCount / columns).ceil();
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

/// Cible Année en mode Qcm (V2, section 12.5) — mêmes tuiles que _QcmAnswers, mais les options sont
/// de simples années en texte (anneeOptions), pas des morceaux : pas de champ à choisir selon la
/// cible, la valeur affichée EST la réponse à soumettre.
class _AnneeQcmAnswers extends StatelessWidget {
  const _AnneeQcmAnswers({required this.anneeOptions, required this.disabled, required this.onSubmit, this.optionsRetirees});

  final List<String> anneeOptions;
  final bool disabled;
  final Future<void> Function(String) onSubmit;

  /// V2, section 12.7 — années retirées par le joker (50/50), jamais la bonne réponse.
  final List<String>? optionsRetirees;

  @override
  Widget build(BuildContext context) {
    final anneesAffichees =
        optionsRetirees == null ? anneeOptions : anneeOptions.where((a) => !optionsRetirees!.contains(a)).toList();
    return FillHeightList(
      itemCount: anneesAffichees.length,
      itemBuilder: (context, index) {
        final annee = anneesAffichees[index];
        return _AnswerTile(label: annee, index: index, onPressed: disabled ? null : () => onSubmit(annee));
      },
    );
  }
}

/// Cible Année en mode saisie (V2, section 12.5) — clavier numérique plutôt que le clavier texte
/// complet de _TextAnswer, pour une réponse qui n'est jamais qu'un nombre à 4 chiffres.
class _AnneeInput extends StatelessWidget {
  const _AnneeInput({required this.controller, required this.disabled, required this.onSubmit, this.decennie});

  final TextEditingController controller;
  final bool disabled;
  final Future<void> Function(String) onSubmit;

  /// V2, section 12.7 — décennie révélée par le joker (ex. 1980), null tant qu'il n'a pas été utilisé.
  final int? decennie;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        if (decennie != null) ...[
          Text(
            'Indice joker : années $decennie',
            textAlign: TextAlign.center,
            style: const TextStyle(fontWeight: FontWeight.w700, color: BlindifyColors.mustard),
          ),
          const SizedBox(height: 12),
        ],
        TextField(
          controller: controller,
          enabled: !disabled,
          keyboardType: TextInputType.number,
          textAlign: TextAlign.center,
          maxLength: 4,
          style: const TextStyle(fontSize: 32, fontWeight: FontWeight.w800),
          decoration: const InputDecoration(labelText: 'Année (ex. 1986)', counterText: ''),
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

class _TextAnswer extends StatelessWidget {
  const _TextAnswer({
    required this.controller,
    required this.disabled,
    required this.cible,
    required this.onSubmit,
    this.structure,
    this.coverUrl,
  });

  final TextEditingController controller;
  final bool disabled;
  final RoundCible cible;
  final Future<void> Function(String) onSubmit;

  /// V2, section 12.7 — texte masqué (lettres remplacées par `_`, espaces/ponctuation préservés).
  final String? structure;

  /// V2, section 12.7 — pochette floutée déjà résolue en URL complète (Titre/Auteur uniquement).
  final String? coverUrl;

  @override
  Widget build(BuildContext context) {
    final label = switch (cible) {
      RoundCible.titre => 'Titre du morceau',
      RoundCible.auteur => "Nom de l'artiste",
      RoundCible.film => 'Film Disney',
      // Jamais atteint en pratique (cible Année routée vers _AnneeInput, voir _buildAnswerArea) —
      // présent pour l'exhaustivité du switch.
      RoundCible.annee => 'Année',
    };
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (coverUrl != null) ...[
          Center(child: CoverArt(imageUrl: coverUrl, size: 96)),
          const SizedBox(height: 12),
        ],
        if (structure != null) ...[
          Text(
            structure!,
            textAlign: TextAlign.center,
            style: const TextStyle(fontWeight: FontWeight.w700, fontFamily: 'monospace', letterSpacing: 2, color: BlindifyColors.mustard),
          ),
          const SizedBox(height: 12),
        ],
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
