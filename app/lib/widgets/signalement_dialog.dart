import 'package:flutter/material.dart';

import '../models/morceau_joue.dart';
import '../models/raison_signalement.dart';
import '../services/game_connection.dart';

/// Formulaire de signalement (V2, section 12.4) — ouvert depuis la liste « Morceaux joués dans cette
/// partie » des réglages admin, jamais avant le reveal (voir MorceauJoue). Retourne un message de
/// confirmation/erreur à afficher en SnackBar par l'appelant, ou null si l'utilisateur a annulé.
Future<String?> showSignalementDialog(BuildContext context, GameConnection game, MorceauJoue morceau) {
  return showDialog<String>(
    context: context,
    builder: (_) => _SignalementDialog(game: game, morceau: morceau),
  );
}

class _SignalementDialog extends StatefulWidget {
  const _SignalementDialog({required this.game, required this.morceau});

  final GameConnection game;
  final MorceauJoue morceau;

  @override
  State<_SignalementDialog> createState() => _SignalementDialogState();
}

class _SignalementDialogState extends State<_SignalementDialog> {
  RaisonSignalement _raison = RaisonSignalement.pasSaPlace;
  final _commentaireController = TextEditingController();
  bool _enCours = false;
  String? _erreurLocale;

  @override
  void dispose() {
    _commentaireController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: Text('Signaler « ${widget.morceau.titre} »'),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(widget.morceau.artiste, style: Theme.of(context).textTheme.bodySmall),
          const SizedBox(height: 16),
          DropdownButtonFormField<RaisonSignalement>(
            initialValue: _raison,
            decoration: const InputDecoration(labelText: 'Raison'),
            items: RaisonSignalement.values
                .map((r) => DropdownMenuItem(value: r, child: Text(r.libelle)))
                .toList(),
            onChanged: (r) => setState(() => _raison = r ?? _raison),
          ),
          const SizedBox(height: 12),
          TextField(
            controller: _commentaireController,
            decoration: InputDecoration(
              labelText: _raison.commentaireObligatoire ? 'Commentaire (obligatoire)' : 'Commentaire (facultatif)',
            ),
            maxLines: 2,
          ),
          if (_erreurLocale != null) ...[
            const SizedBox(height: 8),
            Text(_erreurLocale!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
          ],
        ],
      ),
      actions: [
        TextButton(onPressed: _enCours ? null : () => Navigator.of(context).pop(), child: const Text('Annuler')),
        FilledButton(
          onPressed: _enCours ? null : _soumettre,
          child: _enCours
              ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2))
              : const Text('Signaler'),
        ),
      ],
    );
  }

  Future<void> _soumettre() async {
    final commentaire = _commentaireController.text.trim();
    if (_raison.commentaireObligatoire && commentaire.isEmpty) {
      setState(() => _erreurLocale = 'Un commentaire est requis pour cette raison.');
      return;
    }

    setState(() {
      _enCours = true;
      _erreurLocale = null;
    });

    final succes = await widget.game.signalerMorceau(widget.morceau.trackId, _raison, commentaire.isEmpty ? null : commentaire);

    if (!mounted) return;
    if (succes) {
      Navigator.of(context).pop('Signalement enregistré.');
    } else {
      setState(() {
        _enCours = false;
        _erreurLocale = widget.game.flagError ?? 'Échec du signalement.';
      });
    }
  }
}
