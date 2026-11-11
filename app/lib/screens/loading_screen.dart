import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/game_connection.dart';
import '../theme.dart';
import '../widgets/game_card.dart';

/// Écran affiché pendant la tentative de reconnexion automatique à la dernière adresse
/// connue (voir GameConnection.init) — disparaît dès que la connexion réussit ou échoue ; le
/// bouton permet de ne pas attendre (la reconnexion continue en arrière-plan).
class LoadingScreen extends StatelessWidget {
  const LoadingScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();
    final serverUrl = game.serverUrl;

    return Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 420),
        child: GameCard(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const SizedBox(
                width: 32,
                height: 32,
                child: CircularProgressIndicator(strokeWidth: 3, color: BlindifyColors.cobalt),
              ),
              const SizedBox(height: 20),
              Text('Connexion en cours...', style: Theme.of(context).textTheme.titleMedium),
              if (serverUrl != null) ...[
                const SizedBox(height: 6),
                Text(serverUrl, style: Theme.of(context).textTheme.bodySmall),
              ],
              // Audit 2026-11-10 : plus jamais d'attente sans issue — la reconnexion continue en
              // arrière-plan, le joueur peut choisir une autre adresse ou scanner un QR.
              const SizedBox(height: 16),
              TextButton(
                onPressed: game.afficherEcranConnexion,
                child: const Text('Choisir un autre serveur'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
