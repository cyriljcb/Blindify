import 'package:flutter/foundation.dart' show TargetPlatform, defaultTargetPlatform, kIsWeb;
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/apk_update.dart';
import '../services/game_connection.dart';
import '../theme.dart';

/// Réglages accessibles en permanence (icône engrenage dans l'en-tête, voir main.dart) —
/// retour utilisateur (2026-08-29) : la connexion automatique au démarrage (voir
/// GameConnection.init) fait qu'on ne voit jamais ConnectScreen en pratique, qui portait
/// jusqu'ici l'unique accès à l'adresse serveur et à la mise à jour de l'app.
Future<void> showSettingsSheet(BuildContext context) {
  return showModalBottomSheet(
    context: context,
    isScrollControlled: true,
    backgroundColor: Colors.transparent,
    builder: (_) => const _SettingsSheet(),
  );
}

class _SettingsSheet extends StatefulWidget {
  const _SettingsSheet();

  @override
  State<_SettingsSheet> createState() => _SettingsSheetState();
}

class _SettingsSheetState extends State<_SettingsSheet> {
  late final TextEditingController _urlController;

  @override
  void initState() {
    super.initState();
    _urlController = TextEditingController(text: context.read<GameConnection>().serverUrl ?? defaultServerUrl);
  }

  @override
  void dispose() {
    _urlController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();

    return Padding(
      padding: EdgeInsets.only(bottom: MediaQuery.of(context).viewInsets.bottom),
      child: Container(
        padding: const EdgeInsets.fromLTRB(20, 12, 20, 28),
        decoration: const BoxDecoration(
          color: BlindifyColors.surface,
          border: Border(top: BorderSide(color: BlindifyColors.ink, width: 3)),
          borderRadius: BorderRadius.vertical(top: Radius.circular(16)),
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Center(
              child: Container(
                width: 40,
                height: 4,
                margin: const EdgeInsets.only(bottom: 16),
                decoration: BoxDecoration(color: BlindifyColors.borderSoft, borderRadius: BorderRadius.circular(2)),
              ),
            ),
            Text('Réglages', style: Theme.of(context).textTheme.headlineSmall),
            const SizedBox(height: 16),
            TextField(
              controller: _urlController,
              decoration: const InputDecoration(
                labelText: 'Adresse du serveur',
                hintText: defaultServerUrl,
              ),
              keyboardType: TextInputType.url,
            ),
            const SizedBox(height: 12),
            FilledButton(
              onPressed: game.connecting
                  ? null
                  : () async {
                      final ok = await game.connect(_urlController.text);
                      if (ok && context.mounted) Navigator.of(context).pop();
                    },
              child: game.connecting
                  ? const SizedBox(
                      height: 20,
                      width: 20,
                      child: CircularProgressIndicator(strokeWidth: 2, color: BlindifyColors.onAccent),
                    )
                  : const Text('Se reconnecter à cette adresse'),
            ),
            if (game.errorMessage != null) ...[
              const SizedBox(height: 8),
              Text(game.errorMessage!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
            ],
            if (!kIsWeb && defaultTargetPlatform == TargetPlatform.android) ...[
              const SizedBox(height: 16),
              OutlinedButton.icon(
                onPressed: () => ouvrirMiseAJourApk(_urlController.text),
                icon: const Icon(Icons.system_update_alt_rounded, size: 18),
                label: const Text("Mettre à jour l'app"),
              ),
            ],
          ],
        ),
      ),
    );
  }
}
