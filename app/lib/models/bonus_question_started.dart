import 'qcm_option.dart';
import 'round_cible.dart';
import 'round_mode.dart';

/// Miroir de `BonusQuestionStartedForPlayersDto` — jamais de champ audio ici
/// (l'audio ralenti n'est joué que côté host).
class BonusQuestionStarted {
  BonusQuestionStarted({
    required this.dureePhaseQuestionMs,
    required this.cible,
    required this.roundId,
    required this.serieIndex,
    required this.mode,
    this.qcmOptions,
    this.estCourse = false,
    this.tempsEcouleMs = 0,
    this.anneeOptions,
  });

  final int dureePhaseQuestionMs;

  /// Identité du BonusRound (V2) — voir BonusStakeOptions.roundId, à renvoyer dans SubmitBonusAnswer.
  final String roundId;

  /// Toujours Titre, sauf morceau "disney" (Film) — voir BonusRoundService.CreerBonusRound.
  final RoundCible cible;

  /// 0-based — voir RoundStarted.serieIndex.
  final int serieIndex;

  /// Tiré aléatoirement comme pour un round classique — voir RoundStarted.mode.
  final RoundMode mode;

  final List<QcmOption>? qcmOptions;

  /// "Course" (Mode.qcm uniquement, retour utilisateur) : le premier qui répond, juste ou faux,
  /// décide seul du sort de sa mise — voir BonusRound.EstCourse côté serveur.
  final bool estCourse;

  /// 0 au démarrage normal de la phase ; temps déjà écoulé à la reconnexion — voir
  /// RoundStarted.tempsEcouleMs.
  final int tempsEcouleMs;

  /// Voir RoundStarted.anneeOptions.
  final List<String>? anneeOptions;

  factory BonusQuestionStarted.fromJson(Map<String, dynamic> json) => BonusQuestionStarted(
        dureePhaseQuestionMs: json['dureePhaseQuestionMs'] as int,
        cible: RoundCibleJson.fromJson(json['cible'] as String),
        roundId: json['roundId'] as String,
        serieIndex: json['serieIndex'] as int,
        mode: RoundModeJson.fromJson(json['mode'] as String),
        qcmOptions: (json['qcmOptions'] as List<dynamic>?)
            ?.map((e) => QcmOption.fromJson(e as Map<String, dynamic>))
            .toList(),
        estCourse: json['estCourse'] as bool? ?? false,
        tempsEcouleMs: json['tempsEcouleMs'] as int? ?? 0,
        anneeOptions: (json['anneeOptions'] as List<dynamic>?)?.map((e) => e as String).toList(),
      );
}
