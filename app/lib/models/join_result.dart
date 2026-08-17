import 'team.dart';

class JoinResult {
  JoinResult({required this.success, this.errorMessage, required this.score, this.teamId, required this.teams});

  final bool success;
  final String? errorMessage;
  final int score;
  final String? teamId;
  final List<Team> teams;

  factory JoinResult.fromJson(Map<String, dynamic> json) => JoinResult(
        success: json['success'] as bool,
        errorMessage: json['errorMessage'] as String?,
        score: json['score'] as int,
        teamId: json['teamId'] as String?,
        teams: (json['teams'] as List<dynamic>? ?? [])
            .map((e) => Team.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}
