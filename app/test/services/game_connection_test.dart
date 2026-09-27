// Tests unitaires purs sur GameConnection (pas de testWidgets/WidgetTester) — voir
// docs/refactor-decisions.md section 4. Ciblent la logique historiquement responsable de retours
// utilisateur : la garde anti-race du mode course, la double consommation de pendingJoinCode, et
// les gardes anti-double-soumission. GameConnection n'a pas de seam d'injection pour le hub
// SignalR ; ces tests construisent l'instance sans jamais appeler connect(), et s'appuient sur
// `_hub?.invoke` (plutôt que `_hub!.invoke`) pour que les méthodes de soumission restent
// utilisables (no-op réseau) sans connexion établie.

import 'package:flutter_test/flutter_test.dart';

import 'package:app/models/bonus_question_started.dart';
import 'package:app/models/bonus_stake_options.dart';
import 'package:app/models/etat_courant_joueur.dart';
import 'package:app/models/round_cible.dart';
import 'package:app/models/round_mode.dart';
import 'package:app/models/round_started.dart';
import 'package:app/services/game_connection.dart';

void main() {
  // Requis par les retours haptiques (Haptique, canal de plateforme) déclenchés par submitAnswer/
  // selectStake/submitBonusAnswer.
  TestWidgetsFlutterBinding.ensureInitialized();

  group('appliquerEtatCourant — resynchronisation à la reconnexion (playtest 2026-09-06)', () {
    final round = RoundStarted(
        mode: RoundMode.tapeReponse, cible: RoundCible.auteur, roundId: 'round-1', dureeFenetreReponseMs: 20000, serieIndex: 1);
    final bonusMise = BonusStakeOptions(paliers: [10, 20, 30, 50], roundId: 'bonus-1', dureePhaseMiseMs: 15000, serieIndex: 1);
    final bonusQuestion = BonusQuestionStarted(
        dureePhaseQuestionMs: 20000, cible: RoundCible.titre, roundId: 'bonus-1', serieIndex: 1, mode: RoundMode.qcm);

    test('etat null et actualiserEcran=true : repli sur le lobby', () {
      final game = GameConnection()..screen = AppScreen.loading;
      game.appliquerEtatCourant(null, actualiserEcran: true);
      expect(game.screen, AppScreen.lobby);
    });

    test('etat null et actualiserEcran=false : écran courant inchangé (réassociation en arrière-plan)', () {
      final game = GameConnection()..screen = AppScreen.round;
      game.appliquerEtatCourant(null, actualiserEcran: false);
      expect(game.screen, AppScreen.round);
    });

    test('round classique en cours, pas encore répondu : rebranche sur l\'écran de round', () {
      final game = GameConnection();
      final etat = EtatCourantJoueur(phase: PhaseJoueur.roundClassique, enPause: false, dejaRepondu: false, round: round);

      game.appliquerEtatCourant(etat, actualiserEcran: true);

      expect(game.screen, AppScreen.round);
      expect(game.currentRound, round);
      expect(game.roundAnswered, isFalse);
      expect(game.paused, isFalse);
    });

    test('round classique déjà répondu avant la coupure : roundAnswered=true, pas de nouvelle saisie', () {
      final game = GameConnection();
      final etat = EtatCourantJoueur(phase: PhaseJoueur.roundClassique, enPause: false, dejaRepondu: true, round: round);

      game.appliquerEtatCourant(etat, actualiserEcran: true);

      expect(game.roundAnswered, isTrue);
    });

    test('appliqué même avec actualiserEcran=false : la phase réelle a pu changer pendant la coupure', () {
      final game = GameConnection()..screen = AppScreen.lobby;
      final etat = EtatCourantJoueur(phase: PhaseJoueur.roundClassique, enPause: false, dejaRepondu: false, round: round);

      game.appliquerEtatCourant(etat, actualiserEcran: false);

      expect(game.screen, AppScreen.round);
    });

    test('mise bonus en cours : rebranche sur l\'écran de mise avec les paliers reçus', () {
      final game = GameConnection();
      final etat = EtatCourantJoueur(phase: PhaseJoueur.bonusMise, enPause: false, dejaRepondu: false, bonusMise: bonusMise);

      game.appliquerEtatCourant(etat, actualiserEcran: true);

      expect(game.screen, AppScreen.bonusStake);
      expect(game.bonusStakeOptions, bonusMise);
      expect(game.bonusStakeEnvoyee, isFalse);
    });

    test('question bonus en cours, mise déjà envoyée : bonusStakeEnvoyee reflète dejaRepondu', () {
      final game = GameConnection();
      final etat = EtatCourantJoueur(phase: PhaseJoueur.bonusQuestion, enPause: true, dejaRepondu: true, bonusQuestion: bonusQuestion);

      game.appliquerEtatCourant(etat, actualiserEcran: true);

      expect(game.screen, AppScreen.bonusQuestion);
      expect(game.bonusQuestion, bonusQuestion);
      expect(game.bonusAnswered, isTrue);
      expect(game.paused, isTrue);
    });
  });


  group('Garde anti-race du mode course (BonusQuestionStarted/BonusResult)', () {
    Map<String, dynamic> payloadBonusQuestionStarted({required bool estCourse}) => {
          'dureePhaseQuestionMs': 20000,
          'cible': 'Titre',
          'roundId': 'bonus-course-1',
          'serieIndex': 0,
          'mode': 'Qcm',
          'qcmOptions': null,
          'estCourse': estCourse,
        };

    Map<String, dynamic> payloadBonusResult() => {
          'trackId': 't1',
          'title': 'Titre du morceau',
          'artist': 'Artiste',
          'coverPath': null,
          'cible': 'Titre',
          'film': '?',
          'resultats': <dynamic>[],
          'estCourse': true,
        };

    test('estCourse=true force immédiatement bonusCourseIntro', () {
      final game = GameConnection();
      game.onBonusQuestionStarted(payloadBonusQuestionStarted(estCourse: true));
      expect(game.screen, AppScreen.bonusCourseIntro);
    });

    test('estCourse=false bascule directement sur bonusQuestion, sans écran d\'intro', () {
      final game = GameConnection();
      game.onBonusQuestionStarted(payloadBonusQuestionStarted(estCourse: false));
      expect(game.screen, AppScreen.bonusQuestion);
    });

    test('après le délai d\'intro, bascule sur bonusQuestion si rien d\'autre ne s\'est produit', () async {
      final game = GameConnection();
      game.onBonusQuestionStarted(payloadBonusQuestionStarted(estCourse: true));
      expect(game.screen, AppScreen.bonusCourseIntro);

      await Future<void>.delayed(const Duration(milliseconds: 2600));
      expect(game.screen, AppScreen.bonusQuestion);
    });

    test('un BonusResult arrivé avant la fin du délai n\'est pas écrasé par le timer différé', () async {
      final game = GameConnection();
      game.onBonusQuestionStarted(payloadBonusQuestionStarted(estCourse: true));
      expect(game.screen, AppScreen.bonusCourseIntro);

      // Un autre joueur répond très vite en mode course : BonusResult arrive avant les 2500ms.
      game.onBonusResult(payloadBonusResult());
      expect(game.screen, AppScreen.bonusResult);

      // Le timer différé de bonusCourseIntro, s'il n'était pas gardé, écraserait cet écran plus
      // récent en repassant sur bonusQuestion une fois son délai écoulé.
      await Future<void>.delayed(const Duration(milliseconds: 2600));
      expect(game.screen, AppScreen.bonusResult);
    });
  });

  group('pendingJoinCode — boîte aux lettres à usage unique', () {
    test('setPendingJoinCode le pose, une lecture+remise à null le consomme', () {
      final game = GameConnection();
      expect(game.pendingJoinCode, isNull);

      game.setPendingJoinCode('ABCDE');
      expect(game.pendingJoinCode, 'ABCDE');

      // Simule la consommation par JoinScreen (initState ou build, voir join_screen.dart).
      game.pendingJoinCode = null;
      expect(game.pendingJoinCode, isNull);
    });
  });

  group('Gardes anti-double-soumission', () {
    // Sans hub, l'envoi échoue après les reprises (délai d'attente de connexion mis à zéro ici) :
    // l'état "envoyé" doit être posé immédiatement, bloquer un second envoi tant que le premier est
    // en vol, puis être annulé avec envoiReponseEchoue pour permettre de réessayer.
    GameConnection sansConnexion() => GameConnection()..delaiAttenteConnexion = Duration.zero;

    test('submitAnswer : le premier appel passe roundAnswered à true, les suivants sont sans effet', () async {
      final game = sansConnexion();
      expect(game.roundAnswered, isFalse);

      final envoi = game.submitAnswer('a');
      expect(game.roundAnswered, isTrue);

      await game.submitAnswer('b'); // ignoré : premier envoi encore en cours
      expect(game.roundAnswered, isTrue);
      await envoi;
    });

    test('submitAnswer : la réponse choisie est mémorisée pendant l\'envoi, oubliée en cas d\'échec', () async {
      final game = sansConnexion();

      final envoi = game.submitAnswer('trackA');
      expect(game.reponseChoisie, 'trackA'); // tuile encadrée « Verrouillé » immédiatement

      await envoi;
      expect(game.reponseChoisie, isNull); // la main est rendue : plus aucune tuile verrouillée
    });

    test('submitAnswer : un envoi en échec rend la main au joueur et le signale', () async {
      final game = sansConnexion();

      await game.submitAnswer('a');
      expect(game.roundAnswered, isFalse);
      expect(game.envoiReponseEchoue, isTrue);
    });

    test('submitAnswer : aucun effet pendant une pause', () async {
      final game = GameConnection();
      game.paused = true;

      await game.submitAnswer('a');
      expect(game.roundAnswered, isFalse);
    });

    test('selectStake : le premier appel passe bonusStakeEnvoyee à true et mémorise le palier', () async {
      final game = sansConnexion();
      expect(game.bonusStakeEnvoyee, isFalse);

      final envoi = game.selectStake(2);
      expect(game.bonusStakeEnvoyee, isTrue);
      expect(game.bonusPalierSelectionne, 2);

      await game.selectStake(0);
      expect(game.bonusPalierSelectionne, 2); // deuxième appel ignoré, palier inchangé
      await envoi;
      expect(game.bonusStakeEnvoyee, isFalse); // échec : nouveau choix possible
      expect(game.envoiReponseEchoue, isTrue);
    });

    test('submitBonusAnswer : le premier appel passe bonusAnswered à true, les suivants sont sans effet', () async {
      final game = sansConnexion();
      expect(game.bonusAnswered, isFalse);

      final envoi = game.submitBonusAnswer('a');
      expect(game.bonusAnswered, isTrue);

      await game.submitBonusAnswer('b');
      expect(game.bonusAnswered, isTrue);
      await envoi;
      expect(game.bonusAnswered, isFalse);
      expect(game.envoiReponseEchoue, isTrue);
    });
  });
}
