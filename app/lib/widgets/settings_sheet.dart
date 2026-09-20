import 'package:flutter/foundation.dart' show TargetPlatform, defaultTargetPlatform, kIsWeb;
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/apk_update.dart';
import '../services/game_connection.dart';
import '../theme.dart';
import 'signalement_dialog.dart';

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
  final _adminPasswordController = TextEditingController();
  bool _adminSectionOuverte = false;
  bool _adminAuthEnCours = false;

  @override
  void initState() {
    super.initState();
    _urlController = TextEditingController(text: context.read<GameConnection>().serverUrl ?? defaultServerUrl);
  }

  @override
  void dispose() {
    _urlController.dispose();
    _adminPasswordController.dispose();
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
                icon: Icon(
                  Icons.system_update_alt_rounded,
                  size: 18,
                  color: game.updateDisponible ? BlindifyColors.mustard : null,
                ),
                label: Text(
                  game.updateDisponible
                      ? "Mettre à jour l'app — nouvelle version disponible"
                      : "Mettre à jour l'app",
                ),
                style: game.updateDisponible
                    ? OutlinedButton.styleFrom(
                        foregroundColor: BlindifyColors.mustard,
                        side: const BorderSide(color: BlindifyColors.mustard, width: 2),
                      )
                    : null,
              ),
            ],
            const SizedBox(height: 8),
            _AdminSection(
              ouverte: _adminSectionOuverte,
              onToggle: () => setState(() => _adminSectionOuverte = !_adminSectionOuverte),
              passwordController: _adminPasswordController,
              authEnCours: _adminAuthEnCours,
              onAuthentifier: () async {
                setState(() => _adminAuthEnCours = true);
                await game.authenticateAdmin(_adminPasswordController.text);
                if (mounted) setState(() => _adminAuthEnCours = false);
              },
            ),
          ],
        ),
      ),
    );
  }
}

/// Section repliée par défaut (retour utilisateur : ne doit pas gêner l'usage normal de l'app par
/// un joueur) — un joueur qui ne connaît pas le mot de passe n'y voit qu'un simple champ, jamais les
/// actions elles-mêmes tant que l'authentification n'a pas réussi.
class _AdminSection extends StatelessWidget {
  const _AdminSection({
    required this.ouverte,
    required this.onToggle,
    required this.passwordController,
    required this.authEnCours,
    required this.onAuthentifier,
  });

  final bool ouverte;
  final VoidCallback onToggle;
  final TextEditingController passwordController;
  final bool authEnCours;
  final Future<void> Function() onAuthentifier;

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        TextButton.icon(
          onPressed: onToggle,
          icon: Icon(game.isAdmin ? Icons.shield_rounded : Icons.lock_outline_rounded, size: 18),
          label: Text(game.isAdmin ? 'Mode admin actif' : 'Mode admin'),
        ),
        if (ouverte) ...[
          const SizedBox(height: 8),
          if (!game.isAdmin) ...[
            TextField(
              controller: passwordController,
              decoration: const InputDecoration(labelText: 'Mot de passe admin'),
              obscureText: true,
              onSubmitted: (_) => onAuthentifier(),
            ),
            const SizedBox(height: 12),
            FilledButton(
              onPressed: authEnCours ? null : onAuthentifier,
              child: authEnCours
                  ? const SizedBox(
                      height: 20,
                      width: 20,
                      child: CircularProgressIndicator(strokeWidth: 2, color: BlindifyColors.onAccent),
                    )
                  : const Text('Activer le mode admin'),
            ),
            if (game.adminError != null) ...[
              const SizedBox(height: 8),
              Text(game.adminError!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
            ],
          ] else ...[
            // Ces actions n'ont aucun effet sur la lecture audio (qui reste exclusive au host web,
            // voir CLAUDE.md) — le serveur diffuse juste l'évènement correspondant à tout le groupe,
            // host web compris, qui réagit comme d'habitude (GameHub.ResoudreSessionHostOuAdmin).
            FilledButton.icon(
              onPressed: game.paused ? game.adminResumeGame : game.adminPauseGame,
              icon: Icon(game.paused ? Icons.play_arrow_rounded : Icons.pause_rounded, size: 18),
              label: Text(game.paused ? 'Reprendre la partie' : 'Mettre en pause'),
            ),
            const SizedBox(height: 8),
            OutlinedButton.icon(
              onPressed: game.adminShowLeaderboard,
              icon: const Icon(Icons.leaderboard_rounded, size: 18),
              label: const Text('Afficher le tableau des scores'),
            ),
            const SizedBox(height: 8),
            OutlinedButton.icon(
              onPressed: () => _confirmerFinDePartie(context, game),
              icon: const Icon(Icons.stop_circle_rounded, size: 18),
              label: const Text('Terminer la partie'),
              style: OutlinedButton.styleFrom(foregroundColor: Theme.of(context).colorScheme.error),
            ),
            const Divider(height: 24),
            Text('Morceaux joués dans cette partie', style: Theme.of(context).textTheme.labelLarge),
            const SizedBox(height: 4),
            if (game.morceauxJoues.isEmpty)
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 8),
                child: Text(
                  'Aucun morceau révélé pour l\'instant.',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              )
            else
              ConstrainedBox(
                constraints: const BoxConstraints(maxHeight: 220),
                child: ListView.separated(
                  shrinkWrap: true,
                  itemCount: game.morceauxJoues.length,
                  separatorBuilder: (_, _) => const Divider(height: 1),
                  itemBuilder: (context, index) {
                    final morceau = game.morceauxJoues[game.morceauxJoues.length - 1 - index]; // plus récent d'abord
                    return ListTile(
                      dense: true,
                      contentPadding: EdgeInsets.zero,
                      title: Text(morceau.titre, maxLines: 1, overflow: TextOverflow.ellipsis),
                      subtitle: Text(morceau.artiste, maxLines: 1, overflow: TextOverflow.ellipsis),
                      trailing: IconButton(
                        icon: const Icon(Icons.flag_outlined),
                        tooltip: 'Signaler ce morceau',
                        onPressed: () async {
                          final message = await showSignalementDialog(context, game, morceau);
                          if (message != null && context.mounted) {
                            ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
                          }
                        },
                      ),
                    );
                  },
                ),
              ),
          ],
        ],
      ],
    );
  }

  Future<void> _confirmerFinDePartie(BuildContext context, GameConnection game) async {
    final confirme = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Terminer la partie ?'),
        content: const Text('La partie en cours sera définitivement arrêtée pour tout le monde.'),
        actions: [
          TextButton(onPressed: () => Navigator.of(ctx).pop(false), child: const Text('Annuler')),
          TextButton(onPressed: () => Navigator.of(ctx).pop(true), child: const Text('Terminer')),
        ],
      ),
    );
    if (confirme == true) await game.adminEndGame();
  }
}
