import 'package:flutter_test/flutter_test.dart';

import 'package:app/main.dart';

void main() {
  testWidgets("L'app démarre sur l'écran de connexion", (WidgetTester tester) async {
    await tester.pumpWidget(const BlindifyApp());
    await tester.pump();

    expect(find.text('Connexion au serveur'), findsOneWidget);
  });
}
