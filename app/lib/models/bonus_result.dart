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

  /// Ce qui doit être mis en avant comme "bonne réponse" à l'écran, selon la cible.
  String get reponseAttendue => switch (cible) {
        'Film' => film,
        'Auteur' => artist,
        _ => title,
      };

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
      );
}
