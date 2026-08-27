import 'package:flutter/foundation.dart' show TargetPlatform, defaultTargetPlatform, kIsWeb;
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:url_launcher/url_launcher.dart';

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

  // L'APK est servi par le backend depuis host/ (voir docs/architecture.md "Mettre à jour l'app
  // Android sans câble") — réutilise l'adresse déjà saisie/mémorisée plutôt que de la retaper.
  Future<void> _ouvrirMiseAJour() async {
    final base = _urlController.text.trim().replaceAll(RegExp(r'/+$'), '');
    if (base.isEmpty) return;
    await launchUrl(Uri.parse('$base/blindify.apk'), mode: LaunchMode.externalApplication);
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
              const Icon(Icons.podcasts_rounded, size: 40, color: BlindifyColors.cobalt),
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
              const SizedBox(height: 16),
              Row(children: [
                const Expanded(child: Divider(color: BlindifyColors.borderSoft)),
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 10),
                  child: Text('ou', style: Theme.of(context).textTheme.bodySmall),
                ),
                const Expanded(child: Divider(color: BlindifyColors.borderSoft)),
              ]),
              const SizedBox(height: 16),
              OutlinedButton.icon(
                onPressed: game.connecting
                    ? null
                    : () => Navigator.of(context).push(
                          MaterialPageRoute(builder: (_) => const QrScanScreen()),
                        ),
                icon: const Icon(Icons.qr_code_scanner_rounded),
                label: const Text('Scanner le QR'),
              ),
              if (!kIsWeb && defaultTargetPlatform == TargetPlatform.android) ...[
                const SizedBox(height: 8),
                TextButton.icon(
                  onPressed: _ouvrirMiseAJour,
                  icon: const Icon(Icons.system_update_alt_rounded, size: 18),
                  label: const Text("Mettre à jour l'app"),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
