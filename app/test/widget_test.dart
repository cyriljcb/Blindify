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
    // Pas de pumpAndSettle : la recherche automatique du serveur lancée au démarrage (voir
    // GameConnection.chercherEtConnecter) affiche un indicateur animé qui ne se stabilise jamais ici.
    await tester.pump();
    await tester.pump(const Duration(seconds: 1));

    // Refonte UI (lot 2) : le scan du QR de la TV est l'action principale du premier lancement.
    expect(find.text('Rejoindre la partie'), findsOneWidget);
    expect(find.text('Scanner le QR de la TV'), findsOneWidget);
  });
}
