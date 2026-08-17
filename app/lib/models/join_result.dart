import 'team.dart';

class PlayerSummary {
  PlayerSummary({required this.playerId, required this.nom, required this.estConnecte, this.teamId});

  final String playerId;
  final String nom;
  final bool estConnecte;
  final String? teamId;

  factory PlayerSummary.fromJson(Map<String, dynamic> json) => PlayerSummary(
        playerId: json['playerId'] as String,
        nom: json['nom'] as String,
        estConnecte: json['estConnecte'] as bool,
        teamId: json['teamId'] as String?,
      );
}

class JoinResult {
  JoinResult({
    required this.success,
    this.errorMessage,
    required this.score,
    this.teamId,
    required this.teams,
    required this.joueurs,
  });

  final bool success;
  final String? errorMessage;
  final int score;
  final String? teamId;
  final List<Team> teams;

  /// Roster complet de la partie (soi-même inclus) — voir JoinGameResultDto côté serveur.
  final List<PlayerSummary> joueurs;

  factory JoinResult.fromJson(Map<String, dynamic> json) => JoinResult(
        success: json['success'] as bool,
        errorMessage: json['errorMessage'] as String?,
        score: json['score'] as int,
        teamId: json['teamId'] as String?,
        teams: (json['teams'] as List<dynamic>? ?? [])
            .map((e) => Team.fromJson(e as Map<String, dynamic>))
            .toList(),
        joueurs: (json['joueurs'] as List<dynamic>? ?? [])
            .map((e) => PlayerSummary.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}
