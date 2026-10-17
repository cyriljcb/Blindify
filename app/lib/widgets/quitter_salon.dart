import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/game_connection.dart';
import '../theme.dart';

/// Retour utilisateur (2026-10-17) — quitter le salon depuis l'app (lobby ou réglages), avec
/// confirmation : en pleine partie, le score est gardé et le joueur peut revenir avec le même code.
/// Renvoie true si le joueur a effectivement quitté.
Future<bool> confirmerQuitterSalon(BuildContext context) async {
  final game = context.read<GameConnection>();
  final enPartie = game.screen != AppScreen.lobby && game.screen != AppScreen.ended;

  final confirme = await showDialog<bool>(
    context: context,
    builder: (dialogContext) => AlertDialog(
      backgroundColor: BlindifyColors.surface,
      title: const Text('Quitter le salon ?'),
      content: Text(
        enPartie
            ? "Ton score est gardé : tu pourras revenir dans la partie avec le code ${game.gameCode ?? ''}."
            : 'Tu pourras rejoindre un autre salon ensuite.',
      ),
      actions: [
        TextButton(onPressed: () => Navigator.of(dialogContext).pop(false), child: const Text('Annuler')),
        FilledButton(
          style: FilledButton.styleFrom(backgroundColor: BlindifyColors.coral),
          onPressed: () => Navigator.of(dialogContext).pop(true),
          child: const Text('Quitter'),
        ),
      ],
    ),
  );

  if (confirme != true) return false;
  await game.quitterPartie();
  return true;
}
