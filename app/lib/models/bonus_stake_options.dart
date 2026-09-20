/// Miroir de `BonusStakeOptionsDto` — les 4 paliers de mise de la série courante,
/// annoncés avant que la question ne soit révélée (mise à l'aveugle, voir
/// docs/architecture.md section 7).
class BonusStakeOptions {
  BonusStakeOptions({
    required this.paliers,
    required this.roundId,
    required this.dureePhaseMiseMs,
    required this.serieIndex,
    this.tempsEcouleMs = 0,
  });

  final List<int> paliers;

  /// Identité du BonusRound (V2), stable entre la phase mise et la phase question — à renvoyer
  /// dans SelectStake/SubmitBonusAnswer. Voir RoundStarted.roundId.
  final String roundId;

  final int dureePhaseMiseMs;

  /// 0-based — voir RoundStarted.serieIndex.
  final int serieIndex;

  /// 0 au démarrage normal de la phase ; temps déjà écoulé à la reconnexion — voir
  /// RoundStarted.tempsEcouleMs.
  final int tempsEcouleMs;

  factory BonusStakeOptions.fromJson(Map<String, dynamic> json) => BonusStakeOptions(
        paliers: (json['paliers'] as List<dynamic>).map((e) => e as int).toList(),
        roundId: json['roundId'] as String,
        dureePhaseMiseMs: json['dureePhaseMiseMs'] as int,
        serieIndex: json['serieIndex'] as int,
        tempsEcouleMs: json['tempsEcouleMs'] as int? ?? 0,
      );
}
