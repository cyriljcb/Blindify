/// Miroir de `BonusStakeOptionsDto` — les 4 paliers de mise de la série courante,
/// annoncés avant que la question ne soit révélée (mise à l'aveugle, voir
/// docs/architecture.md section 7).
class BonusStakeOptions {
  BonusStakeOptions({required this.paliers, required this.dureePhaseMiseMs, required this.serieIndex});

  final List<int> paliers;
  final int dureePhaseMiseMs;

  /// 0-based — voir RoundStarted.serieIndex.
  final int serieIndex;

  factory BonusStakeOptions.fromJson(Map<String, dynamic> json) => BonusStakeOptions(
        paliers: (json['paliers'] as List<dynamic>).map((e) => e as int).toList(),
        dureePhaseMiseMs: json['dureePhaseMiseMs'] as int,
        serieIndex: json['serieIndex'] as int,
      );
}
