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
import '../services/haptique.dart';
import '../theme.dart';
import '../widgets/answer_banner.dart';
import '../widgets/appui_tolerant.dart';
import '../widgets/cover_art.dart';
import '../widgets/qcm_tile.dart';
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

  /// Instant (horloge locale) où la phase se termine côté serveur — audit 2026-11-10 : le décompte
  /// retirait 100 ms fixes à chaque tic et ignorait le temps déjà passé sur l'écran d'intro du mode
  /// course (≈ 2,5 s de trop). Calculé depuis la réception de la phase (GameConnection.phaseRecueA),
  /// recalé à chaque reprise après une pause.
  DateTime? _echeance;

  /// Identifiant de la phase affichée — le même State peut être réutilisé si une reconnexion fait
  /// passer directement au round suivant (main.dart garde la clé d'écran) : tout est alors remis à
  /// zéro (chrono, auto-validation, saisie).
  String? _phaseId;

  bool get _bonus => widget.variant == AnswerPhaseVariant.bonus;

  String? _phaseCourante(GameConnection game) =>
      _bonus ? game.bonusQuestion?.roundId : game.currentRound?.roundId;

  int _restantInitialMs(GameConnection game) {
    final duree = _bonus ? game.bonusQuestion?.dureePhaseQuestionMs : game.currentRound?.dureeFenetreReponseMs;
    final ecoule = _bonus ? game.bonusQuestion?.tempsEcouleMs : game.currentRound?.tempsEcouleMs;
    if (duree == null) return 0;
    // tempsEcouleMs > 0 après une reconnexion en pleine phase (voir EtatCourantJoueur), plus le
    // temps passé depuis la réception (écran d'intro du mode course, rendu tardif).
    final depuisReception = game.paused || game.phaseRecueA == null ? 0 : DateTime.now().difference(game.phaseRecueA!).inMilliseconds;
    return (duree - (ecoule ?? 0) - depuisReception).clamp(0, duree);
  }

  void _initialiserPhase(GameConnection game) {
    _phaseId = _phaseCourante(game);
    _ticker?.cancel();
    _ticker = null;
    _autoSubmitDeclenche = false;
    _alerteJouee = false;
    _dernierTic = null;
    _reponseController.clear();
    _remainingMs = _restantInitialMs(game);
    _echeance = DateTime.now().add(Duration(milliseconds: _remainingMs));
    if (!game.paused) _startTicker();
  }

  @override
  void initState() {
    super.initState();
    _initialiserPhase(context.read<GameConnection>());
  }

  // Marge avant zéro à laquelle la validation auto est tentée, pas au tout dernier tick — sans
  // cette marge, le décompte visuel continue jusqu'à 0 normalement. Particulièrement utile côté
  // bonus : le timer serveur (BonusTimerCoordinator, vérifié toutes les 250ms) détruit cet écran
  // dès l'échéance, attendre pile 0 perdait quasi systématiquement la course contre ce timeout.
  static const _margeAutoSubmitMs = 1000;

  /// Seconde pour laquelle le dernier tic haptique a été joué — un tic par seconde sur les
  /// [_secondesTic] dernières, tant que le joueur n'a pas répondu (refonte UI, lot 1).
  int? _dernierTic;
  static const _secondesTic = 3;

  /// Alerte jouée une seule fois par écran, au passage du chrono au rouge (retour utilisateur
  /// 2026-10-17).
  bool _alerteJouee = false;

  void _ticSiDernieresSecondes() {
    final game = context.read<GameConnection>();
    final dejaRepondu = _bonus ? game.bonusAnswered : game.roundAnswered;
    final totalMs = _bonus ? (game.bonusQuestion?.dureePhaseQuestionMs ?? 0) : (game.currentRound?.dureeFenetreReponseMs ?? 0);
    if (!dejaRepondu && !game.paused && !_alerteJouee && _remainingMs > 0 && _remainingMs <= totalMs * 0.15) {
      _alerteJouee = true;
      Haptique.alerte();
      return;
    }
    final secondes = (_remainingMs / 1000).ceil();
    if (dejaRepondu || game.paused || secondes <= 0 || secondes > _secondesTic || secondes == _dernierTic) return;
    _dernierTic = secondes;
    Haptique.tic();
  }

  void _startTicker() {
    _ticker?.cancel();
    _echeance = DateTime.now().add(Duration(milliseconds: _remainingMs));
    _ticker = Timer.periodic(const Duration(milliseconds: 100), (_) {
      if (!mounted) return;
      final restant = _echeance!.difference(DateTime.now()).inMilliseconds;
      setState(() => _remainingMs = restant < 0 ? 0 : restant);
      _ticSiDernieresSecondes();
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

  /// Nouvelle phase reçue sans changement d'écran (reconnexion directement sur le round suivant) :
  /// repart de zéro, voir [_phaseId].
  void _syncPhase(GameConnection game) {
    final phase = _phaseCourante(game);
    if (phase != null && phase != _phaseId) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) setState(() => _initialiserPhase(context.read<GameConnection>()));
      });
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

  /// « Réponse verrouillée · 5/8 ont répondu » — le total est le nombre de joueurs du salon, comme
  /// sur l'écran public. Au moins 1 : le PlayerAnswered du joueur lui-même peut arriver juste après
  /// l'accusé de sa propre réponse.
  static String _texteVerrouille(GameConnection game) {
    // Seuls les joueurs encore attendus (connectés, pas partis) — sinon le compteur n'atteignait
    // jamais le total (audit 2026-11-10).
    final total = game.players.where((p) => p.estConnecte && !p.aQuitte).length;
    final repondus = game.repondants.length.clamp(1, total > 0 ? total : 1);
    return total > 0 ? 'Réponse verrouillée · $repondus/$total ont répondu' : 'Réponse verrouillée';
  }

  /// « Série B · Rock » — le thème n'est connu que par l'annonce de série (SerieAnnoncee) ; absent
  /// après une reconnexion en pleine série, auquel cas seule la lettre est affichée.
  static String _libelleSerie(GameConnection game, int serieIndex) {
    final intro = game.serieIntro;
    final lettre = 'Série ${lettreSerie(serieIndex)}';
    return intro != null && intro.serieIndex == serieIndex ? '$lettre · ${libelleTheme(intro.tags)}' : lettre;
  }

  // Refonte UI (lot 2) : plus de carte (bordure + ombre + marges) pendant la phase de réponse — la
  // question et les options sont posées directement sur le fond, pour laisser un maximum de place
  // aux tuiles (et au champ de saisie clavier ouvert). Les autres écrans gardent leur GameCard.
  Widget _buildClassique(BuildContext context, GameConnection game) {
    final round = game.currentRound;
    if (round == null) {
      return const Center(child: Text('En attente du round...'));
    }

    _syncPhase(game);
    _syncTickerWithPause(game.paused);
    final disabled = game.roundAnswered || game.paused;
    final progress = round.dureeFenetreReponseMs > 0 ? _remainingMs / round.dureeFenetreReponseMs : 0.0;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _EnTeteQuestion(
          contexte: '${_libelleSerie(game, round.serieIndex)} · ${round.mode.label}',
          question: round.cible.question,
        ),
        const SizedBox(height: 12),
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
        const SizedBox(height: 12),
        if (game.paused) const AnswerBanner(text: 'Partie en pause — en attente du host.', color: BlindifyColors.warn),
        if (game.roundAnswered && !game.paused) AnswerBanner(text: _texteVerrouille(game), color: BlindifyColors.good),
        if (game.envoiReponseEchoue && !game.roundAnswered && !game.paused)
          const AnswerBanner(text: 'Réponse non reçue par le serveur (connexion) — réessaie.', color: BlindifyColors.bad),
        if (game.jokerErreur != null && !game.roundAnswered && !game.paused)
          AnswerBanner(text: game.jokerErreur!, color: BlindifyColors.bad),
        Expanded(
          child: _buildAnswerArea(
            mode: round.mode,
            cible: round.cible,
            qcmOptions: round.qcmOptions,
            anneeOptions: round.anneeOptions,
            disabled: disabled,
            repondu: game.roundAnswered,
            reponseChoisie: game.reponseChoisie,
            onSubmit: game.submitAnswer,
            jokerIndice: game.jokerIndiceActuel,
          ),
        ),
      ],
    );
  }

  Widget _buildBonus(BuildContext context, GameConnection game) {
    final question = game.bonusQuestion;
    if (question == null) {
      return const Center(child: Text('En attente de la question...'));
    }

    _syncPhase(game);
    _syncTickerWithPause(game.paused);
    final disabled = game.bonusAnswered || game.paused;
    final progress = question.dureePhaseQuestionMs > 0 ? _remainingMs / question.dureePhaseQuestionMs : 0.0;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _EnTeteQuestion(
          contexte: 'Question bonus · ${_libelleSerie(game, question.serieIndex)} · ${question.mode.label}',
          question: question.cible.question,
          // Mode course : l'écran d'annonce (BonusCourseIntroScreen) a déjà détaillé la règle juste
          // avant, un simple badge suffit ici (retour utilisateur : un pavé écrasait les boutons).
          badge: question.estCourse ? const _CourseBadge() : null,
        ),
        const SizedBox(height: 4),
        Text(
          'Morceau ralenti · un seul essai',
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.bodySmall,
        ),
        const SizedBox(height: 12),
        TimerBar(progress: progress, secondesRestantes: (_remainingMs / 1000).ceil()),
        const SizedBox(height: 12),
        if (game.paused) const AnswerBanner(text: 'Partie en pause — en attente du host.', color: BlindifyColors.warn),
        if (game.bonusAnswered && !game.paused) AnswerBanner(text: _texteVerrouille(game), color: BlindifyColors.good),
        if (game.envoiReponseEchoue && !game.bonusAnswered && !game.paused)
          const AnswerBanner(text: 'Réponse non reçue par le serveur (connexion) — réessaie.', color: BlindifyColors.bad),
        Expanded(
          child: _buildAnswerArea(
            mode: question.mode,
            cible: question.cible,
            qcmOptions: question.qcmOptions,
            anneeOptions: question.anneeOptions,
            disabled: disabled,
            repondu: game.bonusAnswered,
            reponseChoisie: game.reponseChoisie,
            onSubmit: game.submitBonusAnswer,
          ),
        ),
      ],
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
    required bool repondu,
    required String? reponseChoisie,
    required Future<void> Function(String) onSubmit,
    JokerIndice? jokerIndice,
  }) {
    if (cible == RoundCible.annee) {
      return mode == RoundMode.qcm
          ? _AnneeQcmAnswers(
              anneeOptions: anneeOptions ?? [],
              disabled: disabled,
              repondu: repondu,
              reponseChoisie: reponseChoisie,
              onSubmit: onSubmit,
              optionsRetirees: jokerIndice?.optionsRetirees,
            )
          : _AnneeInput(controller: _reponseController, disabled: disabled, onSubmit: onSubmit, decennie: jokerIndice?.decennie);
    }

    return switch (mode) {
      RoundMode.qcm => _QcmAnswers(
          qcmOptions: qcmOptions ?? [],
          cible: cible,
          disabled: disabled,
          repondu: repondu,
          reponseChoisie: reponseChoisie,
          onSubmit: onSubmit,
          optionsRetirees: jokerIndice?.optionsRetirees,
        ),
      RoundMode.premiereLettre => _LetterAnswer(
          disabled: disabled,
          repondu: repondu,
          reponseChoisie: reponseChoisie,
          onSubmit: onSubmit,
          tuilesRestantes: jokerIndice?.tuilesRestantes,
        ),
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
      child: const Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.flag_rounded, size: 14, color: BlindifyColors.mustard),
          SizedBox(width: 4),
          Text('Course', style: TextStyle(fontWeight: FontWeight.w700, fontSize: 12, color: BlindifyColors.mustard)),
        ],
      ),
    );
  }
}

/// En-tête de la phase de réponse (refonte UI, lot 2) : le contexte en petit (série, mode), puis
/// la question en très gros, comme sur l'écran public.
class _EnTeteQuestion extends StatelessWidget {
  const _EnTeteQuestion({required this.contexte, required this.question, this.badge});

  final String contexte;
  final String question;
  final Widget? badge;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Text(
          contexte.toUpperCase(),
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.titleSmall?.copyWith(fontSize: 10.5),
        ),
        const SizedBox(height: 4),
        Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Flexible(
              child: Text(
                question.toUpperCase(),
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                  fontSize: 34,
                  height: 1,
                  shadows: const [Shadow(color: BlindifyColors.coral, offset: Offset(3, 3))],
                ),
              ),
            ),
            if (badge != null) ...[const SizedBox(width: 10), badge!],
          ],
        ),
      ],
    );
  }
}

/// État d'une tuile à partir de ce que le joueur a envoyé — la tuile choisie reste encadrée
/// « Verrouillé », les autres s'estompent (refonte UI, lot 1). Réponse envoyée mais inconnue
/// (reconnexion en pleine phase) : toutes estompées.
EtatTuileQcm _etatTuile({required bool repondu, required String? reponseChoisie, required String valeur}) {
  if (!repondu) return EtatTuileQcm.normale;
  return reponseChoisie == valeur ? EtatTuileQcm.choisie : EtatTuileQcm.estompee;
}

class _QcmAnswers extends StatelessWidget {
  const _QcmAnswers({
    required this.qcmOptions,
    required this.cible,
    required this.disabled,
    required this.repondu,
    required this.reponseChoisie,
    required this.onSubmit,
    this.optionsRetirees,
  });

  final List<QcmOption> qcmOptions;
  final RoundCible cible;
  final bool disabled;
  final bool repondu;
  final String? reponseChoisie;
  final Future<void> Function(String) onSubmit;

  /// V2, section 12.7 — TrackId des options retirées par le joker (50/50), jamais la bonne réponse.
  final List<String>? optionsRetirees;

  @override
  Widget build(BuildContext context) {
    final tuiles = <Widget>[];
    for (var index = 0; index < qcmOptions.length; index++) {
      final option = qcmOptions[index];
      if (optionsRetirees?.contains(option.optionId) ?? false) continue;
      // Un seul champ affiché par option (titre, premier auteur, ou film), pas plusieurs — un
      // morceau à plusieurs auteurs listés en entier rend le QCM illisible.
      // Texte déjà calculé par le serveur pour la cible (premier artiste crédité, film...) — plus de
      // découpage local sur la virgule, qui coupait "Earth, Wind & Fire" (audit 2026-11-10).
      final label = switch (cible) {
        RoundCible.titre => option.texte,
        RoundCible.auteur => option.texte,
        RoundCible.film => option.texte,
        // Jamais atteint en pratique (cible Année routée vers _AnneeQcmAnswers, voir
        // _buildAnswerArea) — présent pour l'exhaustivité du switch.
        RoundCible.annee => option.texte,
      };
      tuiles.add(QcmTile(
        label: label,
        index: index,
        ordreApparition: tuiles.length,
        etat: _etatTuile(repondu: repondu, reponseChoisie: reponseChoisie, valeur: option.optionId),
        onPressed: disabled ? null : () => onSubmit(option.optionId),
      ));
    }
    return GrilleQcm(tuiles: tuiles);
  }
}

class _LetterAnswer extends StatelessWidget {
  const _LetterAnswer({
    required this.disabled,
    required this.repondu,
    required this.reponseChoisie,
    required this.onSubmit,
    this.tuilesRestantes,
  });

  final bool disabled;
  final bool repondu;
  final String? reponseChoisie;
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
          // Grille dimensionnée pour tenir entière à l'écran : sans défilement, aucun geste de
          // glissement ne vient annuler l'appui sur une lettre (voir AppuiTolerant).
          physics: const NeverScrollableScrollPhysics(),
          gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
            crossAxisCount: crossAxisCount,
            mainAxisSpacing: _spacing,
            crossAxisSpacing: _spacing,
            childAspectRatio: tileWidth / tileHeight,
          ),
          itemCount: lettres.length,
          itemBuilder: (context, index) {
            final letter = lettres[index];
            // Même logique que les tuiles QCM : lettre envoyée encadrée en blanc épais, les
            // autres estompées une fois la réponse envoyée.
            final etat = _etatTuile(repondu: repondu, reponseChoisie: reponseChoisie, valeur: letter);
            final choisie = etat == EtatTuileQcm.choisie;
            return AnimatedOpacity(
              opacity: etat == EtatTuileQcm.estompee ? 0.3 : 1,
              duration: BlindifyMotion.fast,
              child: Semantics(
                button: true,
                enabled: !disabled,
                selected: choisie,
                label: 'Lettre $letter',
                excludeSemantics: true,
                child: AppuiTolerant(
                onTap: disabled ? null : () => onSubmit(letter),
                child: Container(
                  decoration: BoxDecoration(
                    color: choisie ? BlindifyColors.cobalt : BlindifyColors.surfaceAlt,
                    borderRadius: BorderRadius.circular(8),
                    border: Border.all(color: BlindifyColors.ink, width: choisie ? 4 : 2),
                  ),
                  alignment: Alignment.center,
                  child: Text(letter, style: const TextStyle(fontSize: 26, fontWeight: FontWeight.w800)),
                ),
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
  const _AnneeQcmAnswers({
    required this.anneeOptions,
    required this.disabled,
    required this.repondu,
    required this.reponseChoisie,
    required this.onSubmit,
    this.optionsRetirees,
  });

  final List<String> anneeOptions;
  final bool disabled;
  final bool repondu;
  final String? reponseChoisie;
  final Future<void> Function(String) onSubmit;

  /// V2, section 12.7 — années retirées par le joker (50/50), jamais la bonne réponse.
  final List<String>? optionsRetirees;

  @override
  Widget build(BuildContext context) {
    final tuiles = <Widget>[];
    for (var index = 0; index < anneeOptions.length; index++) {
      final annee = anneeOptions[index];
      if (optionsRetirees?.contains(annee) ?? false) continue;
      tuiles.add(QcmTile(
        label: annee,
        index: index,
        ordreApparition: tuiles.length,
        etat: _etatTuile(repondu: repondu, reponseChoisie: reponseChoisie, valeur: annee),
        onPressed: disabled ? null : () => onSubmit(annee),
      ));
    }
    return GrilleQcm(tuiles: tuiles);
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

  static final _anneeValide = RegExp(r'^\d{4}$');

  @override
  Widget build(BuildContext context) {
    // Audit 2026-11-10 : un envoi vide consommait l'unique essai avec la pénalité d'une mauvaise
    // réponse — Valider reste grisé tant que l'année n'a pas 4 chiffres.
    return ValueListenableBuilder<TextEditingValue>(
      valueListenable: controller,
      builder: (context, valeur, _) {
        final peutValider = !disabled && _anneeValide.hasMatch(valeur.text.trim());
        return SingleChildScrollView(
          child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
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
          onSubmitted: peutValider ? (value) => onSubmit(value.trim()) : null,
        ),
        const SizedBox(height: 12),
        FilledButton(
          onPressed: peutValider ? () => onSubmit(controller.text.trim()) : null,
          child: const Text('Valider'),
        ),
      ],
          ),
        );
      },
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
      RoundCible.film => 'Film ou série',
      // Jamais atteint en pratique (cible Année routée vers _AnneeInput, voir _buildAnswerArea) —
      // présent pour l'exhaustivité du switch.
      RoundCible.annee => 'Année',
    };
    // Clavier ouvert : la pochette du joker est masquée et la zone défile, pour que le champ et
    // « Valider » restent visibles sur un petit écran (audit 2026-11-10).
    final clavierOuvert = MediaQuery.viewInsetsOf(context).bottom > 0;
    return ValueListenableBuilder<TextEditingValue>(
      valueListenable: controller,
      builder: (context, valeur, _) {
        final peutValider = !disabled && valeur.text.trim().isNotEmpty;
        return SingleChildScrollView(
          child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (coverUrl != null && !clavierOuvert) ...[
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
          // L'autocorrection « corrigeait » les noms d'artistes et les titres anglais.
          autocorrect: false,
          enableSuggestions: false,
          textCapitalization: TextCapitalization.none,
          maxLength: 200,
          decoration: InputDecoration(labelText: label, counterText: ''),
          onSubmitted: peutValider ? (value) => onSubmit(value.trim()) : null,
        ),
        const SizedBox(height: 12),
        FilledButton(
          onPressed: peutValider ? () => onSubmit(controller.text.trim()) : null,
          child: const Text('Valider'),
        ),
      ],
          ),
        );
      },
    );
  }
}
