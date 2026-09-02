class BonusResultEntry {
  BonusResultEntry({
    required this.playerId,
    required this.mise,
    this.reponse,
    required this.estCorrecte,
    required this.points,
  });

  final String playerId;
  final int mise;
  final String? reponse;
  final bool estCorrecte;
  final int points;

  factory BonusResultEntry.fromJson(Map<String, dynamic> json) => BonusResultEntry(
        playerId: json['playerId'] as String,
        mise: json['mise'] as int,
        reponse: json['reponse'] as String?,
        estCorrecte: json['estCorrecte'] as bool,
        points: json['points'] as int,
      );
}

class BonusResult {
  BonusResult({
    required this.trackId,
    required this.title,
    required this.artist,
    this.coverPath,
    required this.cible,
    required this.film,
    required this.resultats,
    this.estCourse = false,
  });

  final String trackId;
  final String title;
  final String artist;
  final String? coverPath;

  /// "Titre", "Auteur" ou "Film" (voir RoundCible côté serveur).
  final String cible;

  /// Nom du film nettoyé (voir FilmNameResolver côté backend) — pertinent seulement si
  /// `cible == 'Film'`.
  final String film;

  final List<BonusResultEntry> resultats;

  /// "Course" (retour utilisateur) : seul le premier qui a répondu apparaît dans `resultats` — les
  /// autres n'ont ni gagné ni perdu leur mise. Voir BonusRound.EstCourse côté serveur.
  final bool estCourse;

  factory BonusResult.fromJson(Map<String, dynamic> json) => BonusResult(
        trackId: json['trackId'] as String,
        title: json['title'] as String,
        artist: json['artist'] as String,
        coverPath: json['coverPath'] as String?,
        cible: json['cible'] as String,
        film: json['film'] as String,
        resultats: (json['resultats'] as List<dynamic>)
            .map((e) => BonusResultEntry.fromJson(e as Map<String, dynamic>))
            .toList(),
        estCourse: json['estCourse'] as bool? ?? false,
      );
}
