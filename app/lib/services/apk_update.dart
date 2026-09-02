import 'package:url_launcher/url_launcher.dart';

/// Ouvre l'APK servi par le backend (voir docs/architecture.md "Mettre à jour l'app Android
/// sans câble") dans le navigateur — téléchargement puis installation par-dessus (signature
/// debug stable d'un build à l'autre sur le poste qui build).
Future<void> ouvrirMiseAJourApk(String baseUrl) async {
  final base = baseUrl.trim().replaceAll(RegExp(r'/+$'), '');
  if (base.isEmpty) return;
  await launchUrl(Uri.parse('$base/blindify.apk'), mode: LaunchMode.externalApplication);
}
