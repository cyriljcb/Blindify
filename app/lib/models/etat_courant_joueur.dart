import 'bonus_question_started.dart';
import 'bonus_stake_options.dart';
import 'round_started.dart';

/// Miroir de `Blindify.Domain.Enums.PhaseJoueur`.
enum PhaseJoueur { aucune, roundClassique, bonusMise, bonusQuestion }

extension PhaseJoueurJson on PhaseJoueur {
  static PhaseJoueur fromJson(String value) {
    switch (value) {
      case 'Aucune':
        return PhaseJoueur.aucune;
      case 'RoundClassique':
        return PhaseJoueur.roundClassique;
      case 'BonusMise':
        return PhaseJoueur.bonusMise;
      case 'BonusQuestion':
        return PhaseJoueur.bonusQuestion;
    }
    throw ArgumentError('PhaseJoueur inconnue reçue du serveur : $value');
  }
}

/// Miroir de `EtatCourantJoueurDto` — renvoyé par JoinGame pour resynchroniser un joueur qui
/// (re)rejoint en pleine partie sur la phase actuellement active (round classique ou question
/// bonus), plutôt que de le laisser au lobby en attendant le prochain événement serveur. Retour
/// utilisateur (playtest 2026-09-06) : un joueur déconnecté en pleine manche ne pouvait sinon
/// participer qu'à partir de la manche suivante.
class EtatCourantJoueur {
  EtatCourantJoueur({
    required this.phase,
    required this.enPause,
    required this.dejaRepondu,
    this.round,
    this.bonusMise,
    this.bonusQuestion,
  });

  final PhaseJoueur phase;
  final bool enPause;

  /// Ce joueur a déjà soumis une réponse/mise pour la phase en cours — ne pas rouvrir la saisie
  /// localement, même si un seul essai par round est de toute façon déjà imposé côté serveur.
  final bool dejaRepondu;

  final RoundStarted? round;
  final BonusStakeOptions? bonusMise;
  final BonusQuestionStarted? bonusQuestion;

  factory EtatCourantJoueur.fromJson(Map<String, dynamic> json) => EtatCourantJoueur(
        phase: PhaseJoueurJson.fromJson(json['phase'] as String),
        enPause: json['enPause'] as bool,
        dejaRepondu: json['dejaRepondu'] as bool,
        round: json['round'] != null ? RoundStarted.fromJson(json['round'] as Map<String, dynamic>) : null,
        bonusMise: json['bonusMise'] != null ? BonusStakeOptions.fromJson(json['bonusMise'] as Map<String, dynamic>) : null,
        bonusQuestion: json['bonusQuestion'] != null
            ? BonusQuestionStarted.fromJson(json['bonusQuestion'] as Map<String, dynamic>)
            : null,
      );
}
