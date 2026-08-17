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
    required this.resultats,
  });

  final String trackId;
  final String title;
  final String artist;
  final String? coverPath;
  final List<RoundResultEntry> resultats;

  factory RoundEnded.fromJson(Map<String, dynamic> json) => RoundEnded(
        trackId: json['trackId'] as String,
        title: json['title'] as String,
        artist: json['artist'] as String,
        coverPath: json['coverPath'] as String?,
        resultats: (json['resultats'] as List<dynamic>)
            .map((e) => RoundResultEntry.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}
