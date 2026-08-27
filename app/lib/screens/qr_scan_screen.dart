import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:mobile_scanner/mobile_scanner.dart';
import 'package:provider/provider.dart';

import '../services/game_connection.dart';
import '../theme.dart';

/// Scan du QR affiché sur l'écran public pendant le lobby (voir host/display.js) — payload JSON
/// `{"server": "...", "code": "..."}`. Poussé en modal par-dessus ConnectScreen (Navigator.push),
/// sans ajouter d'AppScreen dédié : c'est un sous-flux local à l'écran de connexion, pas un état
/// piloté par GameConnection.
class QrScanScreen extends StatefulWidget {
  const QrScanScreen({super.key});

  @override
  State<QrScanScreen> createState() => _QrScanScreenState();
}

class _QrScanScreenState extends State<QrScanScreen> {
  final MobileScannerController _controller = MobileScannerController();
  bool _traitementEnCours = false;
  String? _erreur;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _onDetect(BarcodeCapture capture) async {
    if (_traitementEnCours || capture.barcodes.isEmpty) return;

    final raw = capture.barcodes.first.rawValue;
    if (raw == null) return;

    String? server;
    String? code;
    try {
      final data = jsonDecode(raw) as Map<String, dynamic>;
      server = data['server'] as String?;
      code = data['code'] as String?;
    } catch (_) {
      // QR d'une autre appli / mal formé — on ignore silencieusement et on continue de scanner.
    }

    if (server == null || code == null) {
      setState(() => _erreur = "QR non reconnu — scanne celui affiché sur l'écran public de la partie.");
      return;
    }

    setState(() {
      _traitementEnCours = true;
      _erreur = null;
    });

    final game = context.read<GameConnection>();
    final serveurNettoye = server.trim().replaceAll(RegExp(r'/+$'), '');

    // Scanné depuis JoinScreen alors qu'on est déjà connecté au même serveur (cas le plus courant
    // en pratique : même Raspberry Pi qu'avant, seul le code de partie change à chaque soirée) —
    // pas besoin de rouvrir une connexion, juste récupérer le nouveau code.
    if (game.connected && game.serverUrl == serveurNettoye) {
      game.setPendingJoinCode(code);
      if (mounted) Navigator.of(context).pop();
      return;
    }

    game.pendingJoinCode = code;
    final ok = await game.connect(server);

    if (!mounted) return;
    if (ok) {
      Navigator.of(context).pop();
    } else {
      setState(() {
        _traitementEnCours = false;
        _erreur = game.errorMessage ?? 'Connexion impossible.';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: BlindifyColors.bg,
      appBar: AppBar(
        backgroundColor: BlindifyColors.bg,
        foregroundColor: BlindifyColors.ink,
        title: const Text('Scanner le QR'),
        actions: [
          IconButton(
            icon: const Icon(Icons.flash_on_rounded),
            onPressed: () => _controller.toggleTorch(),
            tooltip: 'Lampe torche',
          ),
        ],
      ),
      body: Stack(
        fit: StackFit.expand,
        children: [
          MobileScanner(controller: _controller, onDetect: _onDetect),
          _ScanFrame(),
          if (_traitementEnCours)
            Container(
              color: Colors.black54,
              child: const Center(
                child: CircularProgressIndicator(color: BlindifyColors.cobalt),
              ),
            ),
          if (_erreur != null)
            Positioned(
              left: 16,
              right: 16,
              bottom: 32,
              child: Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: BlindifyColors.surface,
                  border: Border.all(color: BlindifyColors.coral, width: 2),
                  borderRadius: BorderRadius.circular(8),
                  boxShadow: hardShadow(BlindifyColors.coral, offset: 4),
                ),
                child: Text(
                  _erreur!,
                  textAlign: TextAlign.center,
                  style: const TextStyle(color: BlindifyColors.ink, fontWeight: FontWeight.w600),
                ),
              ),
            ),
        ],
      ),
    );
  }
}

/// Cadre de visée purement décoratif — même langage graphique (bordure épaisse "encre") que le
/// reste de l'app, pour indiquer où pointer la caméra.
class _ScanFrame extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    return Center(
      child: Container(
        width: 240,
        height: 240,
        decoration: BoxDecoration(
          border: Border.all(color: BlindifyColors.mustard, width: 3),
          borderRadius: BorderRadius.circular(16),
        ),
      ),
    );
  }
}
