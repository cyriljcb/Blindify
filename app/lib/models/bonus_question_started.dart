import 'round_cible.dart';

/// Miroir de `BonusQuestionStartedForPlayersDto` — jamais de champ audio ici
/// (l'audio ralenti n'est joué que côté host).
class BonusQuestionStarted {
  BonusQuestionStarted({required this.dureePhaseQuestionMs, required this.cible, required this.serieIndex});

  final int dureePhaseQuestionMs;

  /// Toujours Titre, sauf morceau "disney" (Film) — voir BonusRoundService.CreerBonusRound.
  final RoundCible cible;

  /// 0-based — voir RoundStarted.serieIndex.
  final int serieIndex;

  factory BonusQuestionStarted.fromJson(Map<String, dynamic> json) => BonusQuestionStarted(
        dureePhaseQuestionMs: json['dureePhaseQuestionMs'] as int,
        cible: RoundCibleJson.fromJson(json['cible'] as String),
        serieIndex: json['serieIndex'] as int,
      );
}
