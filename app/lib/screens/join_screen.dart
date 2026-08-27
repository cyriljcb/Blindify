import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/game_connection.dart';
import '../theme.dart';
import '../widgets/game_card.dart';
import 'qr_scan_screen.dart';

class JoinScreen extends StatefulWidget {
  const JoinScreen({super.key});

  @override
  State<JoinScreen> createState() => _JoinScreenState();
}

class _JoinScreenState extends State<JoinScreen> {
  late final TextEditingController _codeController;
  late final TextEditingController _nomController;
  bool _joining = false;

  @override
  void initState() {
    super.initState();
    final game = context.read<GameConnection>();
    // Pré-rempli si on arrive ici après un scan de QR (voir QrScanScreen) — consommé une seule
    // fois pour ne pas re-préremplir un futur passage sur cet écran.
    _codeController = TextEditingController(text: game.pendingJoinCode ?? '');
    game.pendingJoinCode = null;
    _nomController = TextEditingController(text: game.nom ?? '');
  }

  @override
  void dispose() {
    _codeController.dispose();
    _nomController.dispose();
    super.dispose();
  }

  Future<void> _join() async {
    final game = context.read<GameConnection>();
    final nom = _nomController.text.trim();
    final code = _codeController.text.trim().toUpperCase();

    if (nom.isEmpty || code.isEmpty) {
      game.setLocalError('Pseudo et code de partie requis.');
      return;
    }

    setState(() => _joining = true);
    await game.joinGame(code, nom);
    if (mounted) setState(() => _joining = false);
  }

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();

    // Scan lancé depuis cet écran alors qu'on était déjà connecté (voir QrScanScreen) : l'écran
    // était déjà monté, contrairement au cas "scan depuis ConnectScreen" couvert par initState —
    // il faut donc aussi rattraper le code ici, à chaque reconstruction.
    if (game.pendingJoinCode != null) {
      _codeController.text = game.pendingJoinCode!;
      game.pendingJoinCode = null;
    }

    return Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 420),
        child: GameCard(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Icon(Icons.groups_rounded, size: 40, color: BlindifyColors.mustard),
              const SizedBox(height: 12),
              Text('Rejoindre une partie', style: Theme.of(context).textTheme.headlineSmall),
              const SizedBox(height: 20),
              TextField(
                controller: _nomController,
                decoration: const InputDecoration(labelText: 'Ton pseudo'),
                textCapitalization: TextCapitalization.words,
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _codeController,
                decoration: const InputDecoration(labelText: 'Code de la partie'),
                textCapitalization: TextCapitalization.characters,
                style: const TextStyle(letterSpacing: 4, fontWeight: FontWeight.w700),
                onSubmitted: (_) => _join(),
              ),
              const SizedBox(height: 16),
              FilledButton(
                onPressed: _joining ? null : _join,
                child: _joining
                    ? const SizedBox(
                        height: 20,
                        width: 20,
                        child: CircularProgressIndicator(strokeWidth: 2, color: BlindifyColors.onAccent),
                      )
                    : const Text('Rejoindre'),
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
              // Cas le plus fréquent en pratique : déjà connecté au même Raspberry Pi qu'une
              // précédente soirée (reconnexion auto, voir GameConnection.init), seul le code de
              // partie change — ce scan-là ne rouvre pas de connexion (voir QrScanScreen).
              OutlinedButton.icon(
                onPressed: _joining
                    ? null
                    : () => Navigator.of(context).push(
                          MaterialPageRoute(builder: (_) => const QrScanScreen()),
                        ),
                icon: const Icon(Icons.qr_code_scanner_rounded),
                label: const Text('Scanner le QR'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
