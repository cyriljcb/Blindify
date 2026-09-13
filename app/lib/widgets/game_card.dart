import 'package:flutter/material.dart';
import 'package:flutter_animate/flutter_animate.dart';

import '../motion.dart';
import '../theme.dart';

/// Carte "scène" utilisée comme conteneur principal de chaque écran — même esprit que les
/// écrans en carte du host (host/style.css:.screen), pour une identité visuelle cohérente.
class GameCard extends StatelessWidget {
  const GameCard({super.key, required this.child, this.accentColor});

  final Widget child;

  /// Contour + ombre — mustard en mode course (bonus) pour que l'écran bascule visuellement dès
  /// l'annonce plutôt que de répéter une bannière pleine largeur sur l'écran de question (retour
  /// utilisateur : boutons de réponse écrasés par le texte).
  final Color? accentColor;

  @override
  Widget build(BuildContext context) {
    final accent = accentColor ?? BlindifyColors.ink;
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(24),
      decoration: BoxDecoration(
        color: BlindifyColors.surface,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: accent, width: 3),
        boxShadow: hardShadow(accentColor ?? BlindifyColors.cobalt, offset: 7),
      ),
      child: child,
    )
        // Léger dépassement à l'arrivée (échelle 0.96 -> 1) plutôt qu'un simple fade — la carte
        // "atterrit" au lieu d'apparaître platement. main.dart gère déjà la transition ENTRE écrans
        // (AnimatedSwitcher) ; ceci rejoue à chaque fois que GameCard est reconstruite avec un
        // nouveau parent (nouvel écran), sans dépendre l'un de l'autre.
        .animate()
        .fadeIn(duration: BlindifyMotion.fast)
        .scale(begin: const Offset(0.96, 0.96), curve: BlindifyMotion.pop, duration: BlindifyMotion.normal);
  }
}
