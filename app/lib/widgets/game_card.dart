import 'package:flutter/material.dart';

import '../theme.dart';

/// Carte "scène" utilisée comme conteneur principal de chaque écran — même esprit que les
/// écrans en carte du host (host/style.css:.screen), pour une identité visuelle cohérente.
class GameCard extends StatelessWidget {
  const GameCard({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(24),
      decoration: BoxDecoration(
        color: BlindifyColors.surface,
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: BlindifyColors.border),
        boxShadow: [
          BoxShadow(color: Colors.black.withValues(alpha: 0.35), blurRadius: 40, offset: const Offset(0, 20)),
        ],
      ),
      child: child,
    );
  }
}
