import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_animate/flutter_animate.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:provider/provider.dart';

import '../motion.dart';
import '../services/game_connection.dart';
import '../theme.dart';
import '../widgets/answer_banner.dart';
import '../widgets/qcm_tile.dart';

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

    // Refonte UI (lot 2) : sans carte, comme la phase de réponse, et quatre tuiles graduées du plus
    // prudent au plus risqué, avec le gain et la perte possibles (la mise est gagnée ou perdue en
    // entier). Le score du joueur est dans l'en-tête (main.dart:_BandeauJoueur), jamais son rang.
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          'QUESTION BONUS · SÉRIE ${lettreSerie(options.serieIndex)}',
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.titleSmall?.copyWith(fontSize: 10.5),
        ),
        const SizedBox(height: 4),
        Text(
          'COMBIEN TU MISES ?',
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.headlineSmall?.copyWith(
            fontSize: 30,
            height: 1,
            shadows: const [Shadow(color: BlindifyColors.coral, offset: Offset(3, 3))],
          ),
        ),
        const SizedBox(height: 4),
        Text(
          'À l\'aveugle, avant de découvrir la question.',
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.bodySmall,
        ),
        const SizedBox(height: 12),
        TimerBar(progress: progress, secondesRestantes: (_remainingMs / 1000).ceil()),
        const SizedBox(height: 12),
        if (game.paused) const AnswerBanner(text: 'Partie en pause — en attente du host.', color: BlindifyColors.warn),
        if (game.bonusStakeEnvoyee && !game.paused)
          const AnswerBanner(text: 'Mise verrouillée — en attente des autres joueurs.', color: BlindifyColors.good),
        if (game.envoiReponseEchoue && !game.bonusStakeEnvoyee && !game.paused)
          const AnswerBanner(text: 'Mise non reçue par le serveur (connexion) — choisis à nouveau.', color: BlindifyColors.bad),
        Expanded(
          child: Column(
            children: [
              for (var index = 0; index < options.paliers.length; index++) ...[
                if (index > 0) const SizedBox(height: 10),
                Expanded(
                  child: _TuileMise(
                    index: index,
                    valeur: options.paliers[index],
                    etat: !game.bonusStakeEnvoyee
                        ? EtatTuileQcm.normale
                        : game.bonusPalierSelectionne == index
                            ? EtatTuileQcm.choisie
                            : EtatTuileQcm.estompee,
                    onPressed: disabled ? null : () => context.read<GameConnection>().selectStake(index),
                  ),
                ),
              ],
            ],
          ),
        ),
        const SizedBox(height: 8),
        Text(
          'Sans choix dans le délai : ${_nomsPaliers.first}',
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.bodySmall?.copyWith(fontStyle: FontStyle.italic),
        ),
      ],
    );
  }
}

/// Du plus prudent (palier « safe », appliqué par défaut sans choix) au plus risqué.
const _nomsPaliers = ['Prudent', 'Confiant', 'Audacieux', 'Tout ou rien'];

const _fondsPaliers = [
  BlindifyColors.surfaceAlt,
  Color(0x403D5AFF), // cobalt 25 %
  Color(0x38FFC93C), // moutarde 22 %
  Color(0x4DFF4B3E), // corail 30 %
];

class _TuileMise extends StatelessWidget {
  const _TuileMise({required this.index, required this.valeur, required this.etat, required this.onPressed});

  final int index;
  final int valeur;
  final EtatTuileQcm etat;
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) {
    final choisie = etat == EtatTuileQcm.choisie;
    final risque = index == _fondsPaliers.length - 1;
    return AnimatedOpacity(
      opacity: etat == EtatTuileQcm.estompee ? 0.3 : 1,
      duration: BlindifyMotion.fast,
      child: Material(
        color: _fondsPaliers[index.clamp(0, _fondsPaliers.length - 1)],
        borderRadius: BorderRadius.circular(10),
        child: InkWell(
          borderRadius: BorderRadius.circular(10),
          onTap: onPressed,
          child: AnimatedContainer(
            duration: BlindifyMotion.fast,
            padding: const EdgeInsets.symmetric(horizontal: 14),
            decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(10),
              border: Border.all(color: BlindifyColors.ink, width: choisie ? 4 : 2),
              boxShadow: risque && etat != EtatTuileQcm.estompee ? hardShadow(BlindifyColors.coral, offset: 3) : const [],
            ),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    (index < _nomsPaliers.length ? _nomsPaliers[index] : 'Palier ${index + 1}').toUpperCase(),
                    style: Theme.of(context).textTheme.titleMedium?.copyWith(fontSize: 19),
                  ),
                ),
                if (choisie) ...[
                  Container(
                    padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                    decoration: BoxDecoration(color: BlindifyColors.ink, borderRadius: BorderRadius.circular(4)),
                    child: Text(
                      'VERROUILLÉ',
                      style: GoogleFonts.spaceMono(fontSize: 10, fontWeight: FontWeight.w700, color: BlindifyColors.onLight),
                    ),
                  ),
                  const SizedBox(width: 10),
                ],
                Column(
                  mainAxisAlignment: MainAxisAlignment.center,
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    Text('+$valeur', style: GoogleFonts.spaceMono(fontSize: 16, fontWeight: FontWeight.w700, color: BlindifyColors.good)),
                    Text('−$valeur', style: GoogleFonts.spaceMono(fontSize: 12, fontWeight: FontWeight.w700, color: BlindifyColors.bad)),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    ).animate(delay: Duration(milliseconds: 60 * index)).fadeIn(duration: BlindifyMotion.normal).slideY(begin: 0.25, curve: BlindifyMotion.pop);
  }
}
