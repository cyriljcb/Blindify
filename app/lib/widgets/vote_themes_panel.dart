import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../services/game_connection.dart';
import '../theme.dart';
import 'serie_badge.dart';

/// Retour utilisateur (2026-10-17) — vote des thèmes au lobby : chaque joueur choisit jusqu'à
/// `votesParJoueur` thèmes ; chaque toucher renvoie la sélection complète au serveur
/// (GameConnection.voterThemes). Le nombre de voix par thème est affiché, jamais qui a voté quoi.
class VoteThemesPanel extends StatelessWidget {
  const VoteThemesPanel({super.key});

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();
    final vote = game.voteThemes;
    if (vote == null) return const SizedBox.shrink();

    final decompte = game.votesDecompte;
    final choisis = vote.mesVotes;
    final complet = choisis.length >= vote.votesParJoueur;
    final textTheme = Theme.of(context).textTheme;

    final statut = !vote.ouvert
        ? (decompte?.themesRetenus?.isNotEmpty ?? false)
            ? 'Thèmes tirés au sort — regarde l\'écran !'
            : 'Vote clos — le host prépare la partie.'
        : '${choisis.length}/${vote.votesParJoueur} choisi${choisis.length > 1 ? 's' : ''}'
            '${decompte != null ? ' · ${decompte.votants.length}/${game.players.length} ont voté' : ''}';

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: BlindifyColors.surfaceAlt,
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: BlindifyColors.ink, width: 2),
        boxShadow: hardShadow(BlindifyColors.cobalt, offset: 4),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            vote.ouvert ? 'VOTE POUR TES THÈMES (${vote.votesParJoueur} MAX)' : 'THÈMES VOTÉS',
            style: textTheme.titleSmall,
          ),
          const SizedBox(height: 4),
          Text(statut, style: textTheme.bodySmall?.copyWith(color: BlindifyColors.inkDim)),
          const SizedBox(height: 10),
          ConstrainedBox(
            constraints: const BoxConstraints(maxHeight: 190),
            child: SingleChildScrollView(
              child: Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  for (final theme in vote.themes)
                    _ChipTheme(
                      libelle: libelleTheme([theme]),
                      voix: decompte?.nombrePour(theme) ?? 0,
                      selectionne: choisis.contains(theme),
                      actif: vote.ouvert && !game.voteEnvoiEnCours && (choisis.contains(theme) || !complet),
                      onTap: () {
                        final nouveaux = choisis.contains(theme)
                            ? choisis.where((t) => t != theme).toList()
                            : [...choisis, theme];
                        context.read<GameConnection>().voterThemes(nouveaux);
                      },
                    ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _ChipTheme extends StatelessWidget {
  const _ChipTheme({
    required this.libelle,
    required this.voix,
    required this.selectionne,
    required this.actif,
    required this.onTap,
  });

  final String libelle;
  final int voix;
  final bool selectionne;
  final bool actif;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final fond = selectionne ? BlindifyColors.mustard : BlindifyColors.surface;
    final encre = selectionne ? BlindifyColors.bg : BlindifyColors.ink;

    return Opacity(
      opacity: actif || selectionne ? 1 : 0.45,
      child: GestureDetector(
        onTap: actif ? onTap : null,
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 160),
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
          decoration: BoxDecoration(
            color: fond,
            borderRadius: BorderRadius.circular(6),
            border: Border.all(color: BlindifyColors.ink, width: 2),
            boxShadow: selectionne ? hardShadow(BlindifyColors.ink, offset: 3) : null,
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (selectionne) ...[
                Icon(Icons.check_rounded, size: 16, color: encre),
                const SizedBox(width: 4),
              ],
              Text(libelle, style: TextStyle(fontWeight: FontWeight.w700, color: encre)),
              if (voix > 0) ...[
                const SizedBox(width: 6),
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 1),
                  decoration: BoxDecoration(
                    color: selectionne ? BlindifyColors.bg : BlindifyColors.cobalt,
                    borderRadius: BorderRadius.circular(10),
                  ),
                  child: Text(
                    '$voix',
                    style: TextStyle(
                      fontSize: 12,
                      fontWeight: FontWeight.w700,
                      color: selectionne ? BlindifyColors.mustard : BlindifyColors.ink,
                    ),
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
