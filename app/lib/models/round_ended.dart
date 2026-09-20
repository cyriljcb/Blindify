class RoundResultEntry {
  RoundResultEntry({
    required this.playerId,
    this.reponse,
    this.estCorrecte,
    required this.points,
    this.ecartAnnee,
  });

  final String playerId;
  final String? reponse;
  final bool? estCorrecte;
  final int points;

  /// Écart en années (V2, section 12.5) — uniquement pour la cible Année en mode saisie.
  final int? ecartAnnee;

  factory RoundResultEntry.fromJson(Map<String, dynamic> json) => RoundResultEntry(
        playerId: json['playerId'] as String,
        reponse: json['reponse'] as String?,
        estCorrecte: json['estCorrecte'] as bool?,
        points: json['points'] as int,
        ecartAnnee: json['ecartAnnee'] as int?,
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
    this.annee,
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

  /// Année réelle de sortie du morceau (V2, section 12.5) — pertinente seulement si `cible ==
  /// 'Annee'`, jamais transmise avant le reveal (voir RoundEndedDto côté backend).
  final int? annee;

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
        annee: json['annee'] as int?,
      );
}
