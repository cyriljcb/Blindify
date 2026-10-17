/// Vote des thèmes (retour utilisateur 2026-10-17) — miroir de `VoteThemesEtatJoueurDto` (état du vote
/// renvoyé au join/à la reconnexion) et de `VoteThemesOuvertDto` (ouverture par le host).
/// [mesVotes] : ce que CE joueur a choisi, mis à jour localement après un VoterThemes accepté.
class VoteThemesEtat {
  VoteThemesEtat({required this.ouvert, required this.votesParJoueur, required this.themes, required this.mesVotes});

  bool ouvert;
  final int votesParJoueur;
  final List<String> themes;
  List<String> mesVotes;

  factory VoteThemesEtat.fromJson(Map<String, dynamic> json) => VoteThemesEtat(
        ouvert: json['ouvert'] as bool? ?? true,
        votesParJoueur: json['votesParJoueur'] as int,
        themes: (json['themes'] as List<dynamic>? ?? []).cast<String>(),
        mesVotes: (json['mesVotes'] as List<dynamic>? ?? []).cast<String>(),
      );
}

class VoteThemeCompte {
  VoteThemeCompte({required this.theme, required this.nombre});

  final String theme;
  final int nombre;

  factory VoteThemeCompte.fromJson(Map<String, dynamic> json) =>
      VoteThemeCompte(theme: json['theme'] as String, nombre: json['nombre'] as int);
}

/// Miroir de `VotesThemesDto` (événement VotesThemesMisAJour) — jamais qui a voté quoi.
class VotesThemesDecompte {
  VotesThemesDecompte({
    required this.ouvert,
    required this.votes,
    required this.votants,
    required this.themesDistincts,
    this.themesRetenus,
  });

  final bool ouvert;
  final List<VoteThemeCompte> votes;
  final List<String> votants;
  final int themesDistincts;

  /// Thèmes tirés pour la partie, dans l'ordre des séries — null tant que le host n'a pas configuré
  /// la partie depuis les votes.
  final List<String>? themesRetenus;

  int nombrePour(String theme) => votes.where((v) => v.theme == theme).map((v) => v.nombre).firstOrNull ?? 0;

  factory VotesThemesDecompte.fromJson(Map<String, dynamic> json) => VotesThemesDecompte(
        ouvert: json['ouvert'] as bool,
        votes: (json['votes'] as List<dynamic>? ?? [])
            .map((e) => VoteThemeCompte.fromJson(e as Map<String, dynamic>))
            .toList(),
        votants: (json['votants'] as List<dynamic>? ?? []).cast<String>(),
        themesDistincts: json['themesDistincts'] as int? ?? 0,
        themesRetenus: (json['themesRetenus'] as List<dynamic>?)?.cast<String>(),
      );
}
