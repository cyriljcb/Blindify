import 'qcm_option.dart';
import 'round_mode.dart';

/// Miroir de `RoundStartedForPlayersDto` — jamais de champ audio ici, contrairement
/// à la version envoyée au host (`RoundStartedForHostDto`). L'audio ne sort jamais
/// vers les clients joueurs.
class RoundStarted {
  RoundStarted({
    required this.mode,
    required this.dureeFenetreReponseMs,
    this.qcmOptions,
  });

  final RoundMode mode;
  final int dureeFenetreReponseMs;
  final List<QcmOption>? qcmOptions;

  factory RoundStarted.fromJson(Map<String, dynamic> json) => RoundStarted(
        mode: RoundModeJson.fromJson(json['mode'] as String),
        dureeFenetreReponseMs: json['dureeFenetreReponseMs'] as int,
        qcmOptions: (json['qcmOptions'] as List<dynamic>?)
            ?.map((e) => QcmOption.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}
