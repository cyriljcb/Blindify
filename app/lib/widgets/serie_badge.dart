import 'package:flutter/material.dart';

import '../theme.dart';

const _lettres = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';

/// "Série A/B/C..." (index 0-based) — même lettrage que côté host/écran public (retour
/// utilisateur : délimitation claire entre les séries, chacune avec son propre thème).
String lettreSerie(int index) => index >= 0 && index < _lettres.length ? _lettres[index] : '${index + 1}';

/// Équivalent de host/app.js:libelleTheme — "Rock + Années 2010", "Aléatoire" si aucun tag.
String libelleTheme(List<String> tags) {
  if (tags.isEmpty) return 'Aléatoire';
  return tags.map((t) => _capitaliser(t.replaceAll('-', ' '))).join(' + ');
}

String _capitaliser(String texte) => texte.isEmpty ? texte : texte[0].toUpperCase() + texte.substring(1);

/// Petit badge affiché sur les écrans round/bonus pour situer le joueur dans la partie.
class SerieBadge extends StatelessWidget {
  const SerieBadge({super.key, required this.serieIndex});

  final int serieIndex;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(
        color: BlindifyColors.surfaceAlt,
        border: Border.all(color: BlindifyColors.ink, width: 2),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        'Série ${lettreSerie(serieIndex)}',
        style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 12),
      ),
    );
  }
}
