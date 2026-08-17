/// Miroir de `BonusQuestionStartedForPlayersDto` — jamais de champ audio ici
/// (l'audio ralenti n'est joué que côté host).
class BonusQuestionStarted {
  BonusQuestionStarted({required this.dureePhaseQuestionMs});

  final int dureePhaseQuestionMs;

  factory BonusQuestionStarted.fromJson(Map<String, dynamic> json) =>
      BonusQuestionStarted(dureePhaseQuestionMs: json['dureePhaseQuestionMs'] as int);
}
