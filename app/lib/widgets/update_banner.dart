import 'package:flutter/material.dart';
import 'package:flutter_animate/flutter_animate.dart';
import 'package:provider/provider.dart';

import '../motion.dart';
import '../services/apk_update.dart';
import '../services/game_connection.dart';
import '../theme.dart';

/// Bannière "mise à jour disponible" — retour utilisateur : proposer la mise à jour
/// automatiquement plutôt que de compter sur le joueur pour penser à ouvrir les réglages.
/// Visible dès le lancement de l'app sur n'importe quel écran (voir main.dart), fermable pour la
/// session courante (GameConnection.fermerBanniereMiseAJour) — jamais affichée si aucune mise à
/// jour n'est détectée (voir GameConnection.updateDisponible, Android natif uniquement).
class UpdateBanner extends StatelessWidget {
  const UpdateBanner({super.key});

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();
    if (!game.updateDisponible || game.updateBannerFermee) return const SizedBox.shrink();

    return Container(
      width: double.infinity,
      margin: const EdgeInsets.fromLTRB(20, 0, 20, 8),
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
      decoration: BoxDecoration(
        color: BlindifyColors.mustard.withValues(alpha: 0.15),
        border: Border.all(color: BlindifyColors.mustard, width: 2),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Row(
        children: [
          const Icon(Icons.system_update_alt_rounded, color: BlindifyColors.mustard, size: 20),
          const SizedBox(width: 10),
          Expanded(
            child: Text(
              game.updateVersionDistante != null
                  ? 'Nouvelle version disponible (${game.updateVersionDistante})'
                  : 'Nouvelle version disponible',
              style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 13),
            ),
          ),
          TextButton(
            onPressed: () => ouvrirMiseAJourApk(game.serverUrl ?? ''),
            style: TextButton.styleFrom(
              foregroundColor: BlindifyColors.onLight,
              backgroundColor: BlindifyColors.mustard,
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
              minimumSize: Size.zero,
              tapTargetSize: MaterialTapTargetSize.shrinkWrap,
            ),
            child: const Text('Mettre à jour', style: TextStyle(fontWeight: FontWeight.w700, fontSize: 12)),
          ),
          IconButton(
            onPressed: game.fermerBanniereMiseAJour,
            icon: const Icon(Icons.close_rounded, size: 18),
            tooltip: 'Fermer',
            padding: EdgeInsets.zero,
            constraints: const BoxConstraints(minWidth: 32, minHeight: 32),
          ),
        ],
      ),
    ).animate().fadeIn(duration: BlindifyMotion.normal).slideY(begin: -0.4, curve: BlindifyMotion.pop);
  }
}
