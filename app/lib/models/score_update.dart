class PlayerScore {
  PlayerScore({required this.playerId, required this.nom, required this.score, this.teamId});

  final String playerId;
  final String nom;
  final int score;
  final String? teamId;

  factory PlayerScore.fromJson(Map<String, dynamic> json) => PlayerScore(
        playerId: json['playerId'] as String,
        nom: json['nom'] as String,
        score: json['score'] as int,
        teamId: json['teamId'] as String?,
      );
}

class TeamScore {
  TeamScore({required this.teamId, required this.nom, required this.score});

  final String teamId;
  final String nom;
  final int score;

  factory TeamScore.fromJson(Map<String, dynamic> json) => TeamScore(
        teamId: json['teamId'] as String,
        nom: json['nom'] as String,
        score: json['score'] as int,
      );
}

/// Miroir de `ScoreUpdateDto` — `equipes` n'est présent que si le mode équipe est actif
/// (voir docs/architecture.md section 8).
class ScoreUpdate {
  ScoreUpdate({required this.joueurs, this.equipes});

  final List<PlayerScore> joueurs;
  final List<TeamScore>? equipes;

  factory ScoreUpdate.fromJson(Map<String, dynamic> json) => ScoreUpdate(
        joueurs: (json['joueurs'] as List<dynamic>)
            .map((e) => PlayerScore.fromJson(e as Map<String, dynamic>))
            .toList(),
        equipes: (json['equipes'] as List<dynamic>?)
            ?.map((e) => TeamScore.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}
