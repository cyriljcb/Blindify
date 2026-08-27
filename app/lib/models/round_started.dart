import 'qcm_option.dart';
import 'round_cible.dart';
import 'round_mode.dart';

/// Miroir de `RoundStartedForPlayersDto` — jamais de champ audio ici, contrairement
/// à la version envoyée au host (`RoundStartedForHostDto`). L'audio ne sort jamais
/// vers les clients joueurs.
class RoundStarted {
  RoundStarted({
    required this.mode,
    required this.cible,
    required this.dureeFenetreReponseMs,
    required this.serieIndex,
    this.qcmOptions,
  });

  final RoundMode mode;
  final RoundCible cible;
  final int dureeFenetreReponseMs;

  /// 0-based — permet d'afficher "Série A/B/C..." (retour utilisateur : délimitation claire
  /// entre les séries, chacune avec son propre thème).
  final int serieIndex;

  final List<QcmOption>? qcmOptions;

  factory RoundStarted.fromJson(Map<String, dynamic> json) => RoundStarted(
        mode: RoundModeJson.fromJson(json['mode'] as String),
        cible: RoundCibleJson.fromJson(json['cible'] as String),
        dureeFenetreReponseMs: json['dureeFenetreReponseMs'] as int,
        serieIndex: json['serieIndex'] as int,
        qcmOptions: (json['qcmOptions'] as List<dynamic>?)
            ?.map((e) => QcmOption.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}
