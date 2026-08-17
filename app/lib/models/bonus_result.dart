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
    required this.resultats,
  });

  final String trackId;
  final String title;
  final String artist;
  final List<BonusResultEntry> resultats;

  factory BonusResult.fromJson(Map<String, dynamic> json) => BonusResult(
        trackId: json['trackId'] as String,
        title: json['title'] as String,
        artist: json['artist'] as String,
        resultats: (json['resultats'] as List<dynamic>)
            .map((e) => BonusResultEntry.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}
