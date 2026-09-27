import 'package:flutter/foundation.dart'
    show TargetPlatform, defaultTargetPlatform, kIsWeb;
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/apk_update.dart';
import '../services/game_connection.dart';
import '../theme.dart';
import '../widgets/game_card.dart';
import 'qr_scan_screen.dart';

class ConnectScreen extends StatefulWidget {
  const ConnectScreen({super.key});

  @override
  State<ConnectScreen> createState() => _ConnectScreenState();
}

class _ConnectScreenState extends State<ConnectScreen> {
  late final TextEditingController _urlController;
  bool _autresOptions = false;

  @override
  void initState() {
    super.initState();
    _urlController = TextEditingController(
      text: context.read<GameConnection>().serverUrl ?? defaultServerUrl,
    );
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
          // Le bouton QR + le lien de mise à jour APK ont fait déborder la carte sur les écrans
          // courts (overflow constaté en test) — la carte devient scrollable plutôt que de couper
          // le contenu.
          child: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                // Refonte UI (lot 2) : le scan du QR affiché sur la TV passe en action principale
                // — c'est ce que fait tout le monde en soirée ; la saisie d'adresse (utile seulement
                // pour dépanner) est repliée dans « Autres options ».
                const Icon(Icons.qr_code_2_rounded, size: 44, color: BlindifyColors.mustard),
                const SizedBox(height: 12),
                Text('Rejoindre la partie', style: Theme.of(context).textTheme.headlineSmall),
                const SizedBox(height: 4),
                Text('Scanne le QR code affiché sur la TV.', style: Theme.of(context).textTheme.bodyMedium),
                const SizedBox(height: 20),
                FilledButton.icon(
                  onPressed: game.connecting
                      ? null
                      : () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const QrScanScreen())),
                  icon: const Icon(Icons.qr_code_scanner_rounded),
                  label: const Text('Scanner le QR de la TV'),
                ),
                if (game.errorMessage != null) ...[
                  const SizedBox(height: 12),
                  Text(game.errorMessage!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
                ],
                const SizedBox(height: 12),
                TextButton.icon(
                  onPressed: () => setState(() => _autresOptions = !_autresOptions),
                  icon: Icon(_autresOptions ? Icons.expand_less_rounded : Icons.expand_more_rounded),
                  label: const Text('Autres options'),
                ),
                // Ouvert d'office après un échec de connexion manuelle, pour corriger l'adresse.
                if (_autresOptions || game.errorMessage != null) ...[
                  const SizedBox(height: 8),
                  TextField(
                    controller: _urlController,
                    decoration: const InputDecoration(labelText: 'Adresse du serveur', hintText: defaultServerUrl),
                    keyboardType: TextInputType.url,
                    onSubmitted: (_) => context.read<GameConnection>().connect(_urlController.text),
                  ),
                  const SizedBox(height: 12),
                  OutlinedButton(
                    onPressed: game.connecting ? null : () => context.read<GameConnection>().connect(_urlController.text),
                    child: game.connecting
                        ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2))
                        : const Text('Se connecter à cette adresse'),
                  ),
                  if (!kIsWeb && defaultTargetPlatform == TargetPlatform.android) ...[
                    const SizedBox(height: 8),
                    TextButton.icon(
                      onPressed: () => ouvrirMiseAJourApk(_urlController.text),
                      icon: const Icon(Icons.system_update_alt_rounded, size: 18),
                      label: const Text("Mettre à jour l'app"),
                    ),
                  ],
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}
