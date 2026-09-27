import 'package:flutter/material.dart';
import 'package:flutter_animate/flutter_animate.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:provider/provider.dart';

import '../motion.dart';
import '../services/game_connection.dart';
import '../theme.dart';

/// Refonte UI (lot 3) — sur les écrans d'attente (résultat, annonce de série) : l'écart de points
/// avec le joueur juste devant, sans nom ni place ; le premier voit « Tu es en tête ». Rien si
/// l'option est désactivée pour la partie (GameConnection.afficherEcart) ou avant le premier score.
class CarteEcart extends StatelessWidget {
  const CarteEcart({super.key});

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();
    final ecart = game.ecartAvecJoueurDevant;
    if (ecart == null) return const SizedBox.shrink();

    final enTete = ecart == 0;
    final equipe = (game.scoreUpdate?.equipes?.isNotEmpty ?? false) && game.teamId != null;

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
      decoration: BoxDecoration(
        color: BlindifyColors.surfaceAlt,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: BlindifyColors.ink, width: 2),
        boxShadow: hardShadow(enTete ? BlindifyColors.good : BlindifyColors.mustard, offset: 4),
      ),
      child: Column(
        children: [
          Text(
            enTete ? (equipe ? 'TON ÉQUIPE' : 'CLASSEMENT') : (equipe ? "L'ÉQUIPE DEVANT LA TIENNE" : 'LE JOUEUR DEVANT TOI'),
            style: Theme.of(context).textTheme.titleSmall?.copyWith(fontSize: 10.5),
          ),
          const SizedBox(height: 4),
          Text(
            enTete ? 'EN TÊTE' : '+$ecart PTS',
            style: GoogleFonts.anton(fontSize: 34, height: 1, color: enTete ? BlindifyColors.good : BlindifyColors.mustard),
          ),
          if (!enTete) ...[
            const SizedBox(height: 4),
            Text('Une bonne réponse rapide peut suffire.', style: Theme.of(context).textTheme.bodySmall),
          ],
        ],
      ),
    ).animate().fadeIn(duration: BlindifyMotion.normal).slideY(begin: 0.2, curve: BlindifyMotion.pop);
  }
}
