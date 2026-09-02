class RoundResultEntry {
  RoundResultEntry({
    required this.playerId,
    this.reponse,
    this.estCorrecte,
    required this.points,
  });

  final String playerId;
  final String? reponse;
  final bool? estCorrecte;
  final int points;

  factory RoundResultEntry.fromJson(Map<String, dynamic> json) => RoundResultEntry(
        playerId: json['playerId'] as String,
        reponse: json['reponse'] as String?,
        estCorrecte: json['estCorrecte'] as bool?,
        points: json['points'] as int,
      );
}

class RoundEnded {
  RoundEnded({
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

  /// Cible du round ("Titre"/"Auteur"/"Film", voir RoundCible côté serveur) — répétée ici pour
  /// savoir si la réponse attendue était le film plutôt que le titre réel de la chanson.
  final String cible;

  /// Nom du film nettoyé (voir FilmNameResolver côté backend) — pertinent seulement si
  /// `cible == 'Film'`.
  final String film;

  final List<RoundResultEntry> resultats;

  factory RoundEnded.fromJson(Map<String, dynamic> json) => RoundEnded(
        trackId: json['trackId'] as String,
        title: json['title'] as String,
        artist: json['artist'] as String,
        coverPath: json['coverPath'] as String?,
        cible: json['cible'] as String,
        film: json['film'] as String,
        resultats: (json['resultats'] as List<dynamic>)
            .map((e) => RoundResultEntry.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}
