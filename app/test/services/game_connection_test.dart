// Tests unitaires purs sur GameConnection (pas de testWidgets/WidgetTester) — voir
// docs/refactor-decisions.md section 4. Ciblent la logique historiquement responsable de retours
// utilisateur : la garde anti-race du mode course, la double consommation de pendingJoinCode, et
// les gardes anti-double-soumission. GameConnection n'a pas de seam d'injection pour le hub
// SignalR ; ces tests construisent l'instance sans jamais appeler connect(), et s'appuient sur
// `_hub?.invoke` (plutôt que `_hub!.invoke`) pour que les méthodes de soumission restent
// utilisables (no-op réseau) sans connexion établie.

import 'package:flutter_test/flutter_test.dart';

import 'package:app/services/game_connection.dart';

void main() {
  group('Garde anti-race du mode course (BonusQuestionStarted/BonusResult)', () {
    Map<String, dynamic> payloadBonusQuestionStarted({required bool estCourse}) => {
          'dureePhaseQuestionMs': 20000,
          'cible': 'Titre',
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
    test('submitAnswer : le premier appel passe roundAnswered à true, les suivants sont sans effet', () async {
      final game = GameConnection();
      expect(game.roundAnswered, isFalse);

      await game.submitAnswer('a');
      expect(game.roundAnswered, isTrue);

      await game.submitAnswer('b');
      expect(game.roundAnswered, isTrue);
    });

    test('submitAnswer : aucun effet pendant une pause', () async {
      final game = GameConnection();
      game.paused = true;

      await game.submitAnswer('a');
      expect(game.roundAnswered, isFalse);
    });

    test('selectStake : le premier appel passe bonusStakeEnvoyee à true et mémorise le palier', () async {
      final game = GameConnection();
      expect(game.bonusStakeEnvoyee, isFalse);

      await game.selectStake(2);
      expect(game.bonusStakeEnvoyee, isTrue);
      expect(game.bonusPalierSelectionne, 2);

      await game.selectStake(0);
      expect(game.bonusPalierSelectionne, 2); // deuxième appel ignoré, palier inchangé
    });

    test('submitBonusAnswer : le premier appel passe bonusAnswered à true, les suivants sont sans effet', () async {
      final game = GameConnection();
      expect(game.bonusAnswered, isFalse);

      await game.submitBonusAnswer('a');
      expect(game.bonusAnswered, isTrue);

      await game.submitBonusAnswer('b');
      expect(game.bonusAnswered, isTrue);
    });
  });
}
