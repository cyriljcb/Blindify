import 'joker_indice.dart';
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
    required this.roundId,
    required this.dureeFenetreReponseMs,
    required this.serieIndex,
    this.qcmOptions,
    this.tempsEcouleMs = 0,
    this.anneeOptions,
    this.jokerIndice,
  });

  final RoundMode mode;
  final RoundCible cible;

  /// Identité du round (V2) — à renvoyer tel quel dans SubmitAnswer : le serveur ignore
  /// silencieusement toute réponse dont le roundId ne correspond plus au round courant (voir
  /// GameHub.SubmitAnswer/RoundService.SoumettreReponse côté serveur).
  final String roundId;

  final int dureeFenetreReponseMs;

  /// 0-based — permet d'afficher "Série A/B/C..." (retour utilisateur : délimitation claire
  /// entre les séries, chacune avec son propre thème).
  final int serieIndex;

  final List<QcmOption>? qcmOptions;

  /// 0 au démarrage normal du round ; temps déjà écoulé quand ce round est renvoyé via
  /// EtatCourantJoueur (reconnexion en pleine partie) — permet à AnswerPhaseScreen d'initialiser
  /// sa barre de temps sur le temps réellement restant plutôt que de repartir de la durée totale.
  final int tempsEcouleMs;

  /// Les 4 années proposées, en texte, triées (cible Année + mode Qcm uniquement, V2 section
  /// 12.5) — mutuellement exclusif avec [qcmOptions] selon la cible du round.
  final List<String>? anneeOptions;

  /// V2, section 12.7 — toujours null sur un RoundStarted fraîchement diffusé ; rempli uniquement
  /// à la reconstruction du round (reconnexion) si ce joueur avait déjà utilisé son joker sur ce
  /// round avant la coupure, pour rejouer le même indice plutôt qu'un nouveau tirage.
  final JokerIndice? jokerIndice;

  factory RoundStarted.fromJson(Map<String, dynamic> json) => RoundStarted(
        mode: RoundModeJson.fromJson(json['mode'] as String),
        cible: RoundCibleJson.fromJson(json['cible'] as String),
        roundId: json['roundId'] as String,
        dureeFenetreReponseMs: json['dureeFenetreReponseMs'] as int,
        serieIndex: json['serieIndex'] as int,
        qcmOptions: (json['qcmOptions'] as List<dynamic>?)
            ?.map((e) => QcmOption.fromJson(e as Map<String, dynamic>))
            .toList(),
        tempsEcouleMs: json['tempsEcouleMs'] as int? ?? 0,
        anneeOptions: (json['anneeOptions'] as List<dynamic>?)?.map((e) => e as String).toList(),
        jokerIndice: json['jokerIndice'] != null ? JokerIndice.fromJson(json['jokerIndice'] as Map<String, dynamic>) : null,
      );
}
