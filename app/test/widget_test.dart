import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:app/main.dart';

void main() {
  testWidgets("L'app démarre sur l'écran de connexion", (WidgetTester tester) async {
    // GameConnection.init() lit SharedPreferences avant de choisir l'écran (loading → connect
    // en l'absence d'adresse serveur sauvegardée) — sans mock, l'appel de plateforme ne répond
    // jamais et l'app reste bloquée sur l'écran de chargement.
    SharedPreferences.setMockInitialValues({});

    await tester.pumpWidget(const BlindifyApp());
    await tester.pumpAndSettle();

    expect(find.text('Connexion au serveur'), findsOneWidget);
  });
}
