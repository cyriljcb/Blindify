import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/game_connection.dart';
import '../theme.dart';
import '../widgets/game_card.dart';

class ConnectScreen extends StatefulWidget {
  const ConnectScreen({super.key});

  @override
  State<ConnectScreen> createState() => _ConnectScreenState();
}

class _ConnectScreenState extends State<ConnectScreen> {
  late final TextEditingController _urlController;

  @override
  void initState() {
    super.initState();
    _urlController = TextEditingController(text: context.read<GameConnection>().serverUrl ?? '');
  }

  @override
  void dispose() {
    _urlController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();

    return Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 420),
        child: GameCard(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Icon(Icons.podcasts_rounded, size: 40, color: BlindifyColors.accent),
              const SizedBox(height: 12),
              Text('Connexion au serveur', style: Theme.of(context).textTheme.headlineSmall),
              const SizedBox(height: 4),
              Text(
                "Adresse du backend affichée par le host (Raspberry Pi)",
                style: Theme.of(context).textTheme.bodySmall,
              ),
              const SizedBox(height: 20),
              TextField(
                controller: _urlController,
                decoration: const InputDecoration(
                  labelText: 'Adresse du serveur',
                  hintText: 'http://192.168.1.42:5000',
                ),
                keyboardType: TextInputType.url,
                onSubmitted: (_) => context.read<GameConnection>().connect(_urlController.text),
              ),
              const SizedBox(height: 16),
              FilledButton(
                onPressed: game.connecting
                    ? null
                    : () => context.read<GameConnection>().connect(_urlController.text),
                child: game.connecting
                    ? const SizedBox(
                        height: 20,
                        width: 20,
                        child: CircularProgressIndicator(strokeWidth: 2, color: BlindifyColors.onAccent),
                      )
                    : const Text('Se connecter'),
              ),
              if (game.errorMessage != null) ...[
                const SizedBox(height: 12),
                Text(game.errorMessage!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
