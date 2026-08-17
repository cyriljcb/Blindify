import 'package:flutter/material.dart';

import '../theme.dart';

/// Avatar rond coloré par identifiant (même joueur = même couleur d'un écran à l'autre) —
/// même logique que côté host (host/app.js:couleurAvatar) pour une identité visuelle cohérente.
class PlayerAvatar extends StatelessWidget {
  const PlayerAvatar({super.key, required this.id, required this.nom, this.medaille, this.size = 36});

  final String id;
  final String nom;

  /// 0/1/2 -> 🥇🥈🥉 à la place des initiales (classement final / tableau général).
  final int? medaille;
  final double size;

  static const _medailles = ['🥇', '🥈', '🥉'];

  Color _couleur() {
    var hash = 0;
    for (final unit in id.codeUnits) {
      hash = (hash * 31 + unit) & 0x7fffffff;
    }
    return HSLColor.fromAHSL(1, (hash % 360).toDouble(), 0.65, 0.55).toColor();
  }

  @override
  Widget build(BuildContext context) {
    if (medaille != null && medaille! < 3) {
      return SizedBox(
        width: size,
        height: size,
        child: Center(child: Text(_medailles[medaille!], style: TextStyle(fontSize: size * 0.6))),
      );
    }

    final initiale = nom.trim().isEmpty ? '?' : nom.trim().substring(0, 1).toUpperCase();
    return CircleAvatar(
      radius: size / 2,
      backgroundColor: _couleur(),
      child: Text(
        initiale,
        style: TextStyle(color: BlindifyColors.onAccent, fontWeight: FontWeight.w800, fontSize: size * 0.4),
      ),
    );
  }
}
