import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/game_connection.dart';
import '../theme.dart';

/// Palette identique à host/app.js et host/display.js:couleurAvatar — hash%360 laissait parfois
/// deux joueurs avec des teintes de vert trop proches pour être distinguées (retour utilisateur).
const _paletteAvatars = [
  Color(0xFFE63946), Color(0xFF457B9D), Color(0xFFF4A300), Color(0xFF2A9D8F),
  Color(0xFF9B5DE5), Color(0xFF06D6A0), Color(0xFFF15BB5), Color(0xFF4CC9F0),
  Color(0xFFFF6B35), Color(0xFF8AC926), Color(0xFFFFCA3A), Color(0xFF6A4C93),
];

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

  /// Couleur basée sur la position dans le roster (ordre d'arrivée) plutôt que sur un hash de
  /// l'id, pour garantir des couleurs distinctes entre joueurs (et entre équipes) tant que leur
  /// nombre ne dépasse pas la taille de la palette.
  Color _couleur(GameConnection game) {
    final indexJoueur = game.players.indexWhere((p) => p.playerId == id);
    if (indexJoueur >= 0) return _paletteAvatars[indexJoueur % _paletteAvatars.length];

    final indexEquipe = game.teams.indexWhere((t) => t.id == id);
    if (indexEquipe >= 0) return _paletteAvatars[(game.players.length + indexEquipe) % _paletteAvatars.length];

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

    final game = context.watch<GameConnection>();
    final initiale = nom.trim().isEmpty ? '?' : nom.trim().substring(0, 1).toUpperCase();
    return CircleAvatar(
      radius: size / 2,
      backgroundColor: _couleur(game),
      child: Text(
        initiale,
        style: TextStyle(color: BlindifyColors.onAccent, fontWeight: FontWeight.w800, fontSize: size * 0.4),
      ),
    );
  }
}
