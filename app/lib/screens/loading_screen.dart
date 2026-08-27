import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/game_connection.dart';
import '../theme.dart';
import '../widgets/game_card.dart';

/// Écran affiché pendant la tentative de reconnexion automatique à la dernière adresse
/// connue (voir GameConnection.init) — disparaît dès que la connexion réussit ou échoue,
/// jamais d'action possible ici.
class LoadingScreen extends StatelessWidget {
  const LoadingScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final serverUrl = context.watch<GameConnection>().serverUrl;

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
            ],
          ),
        ),
      ),
    );
  }
}
