class JoinResult {
  JoinResult({required this.success, this.errorMessage, required this.score, this.teamId});

  final bool success;
  final String? errorMessage;
  final int score;
  final String? teamId;

  factory JoinResult.fromJson(Map<String, dynamic> json) => JoinResult(
        success: json['success'] as bool,
        errorMessage: json['errorMessage'] as String?,
        score: json['score'] as int,
        teamId: json['teamId'] as String?,
      );
}
