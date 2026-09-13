import 'dart:convert';

import 'package:http/http.dart' as http;
import 'package:package_info_plus/package_info_plus.dart';

/// Résultat de la comparaison entre le build installé et le dernier build déployé côté serveur.
class UpdateCheckResult {
  const UpdateCheckResult({required this.disponible, this.versionDistante});

  final bool disponible;

  /// Nom de version à afficher (ex. "1.0.1") — null si aucune maj n'est disponible ou si la
  /// vérification a échoué.
  final String? versionDistante;

  static const aucuneMiseAJour = UpdateCheckResult(disponible: false);
}

/// Compare le build installé (PackageInfo — reflète pubspec.yaml au moment du `flutter build apk`,
/// voir apk_version.json ci-dessous pour le pendant serveur) au dernier build déployé sur le Pi,
/// via un petit fichier JSON servi à côté de l'APK (host/apk_version.json, réécrit à chaque
/// déploiement — voir docs/architecture.md "Mettre à jour l'app Android sans câble"). Retour
/// utilisateur : proposer la mise à jour automatiquement plutôt que de compter sur le joueur pour
/// penser à vérifier lui-même.
///
/// Comparaison sur buildNumber (entier, incrémenté à chaque déploiement) plutôt que sur le nom de
/// version (chaîne) — une comparaison sémantique de chaînes ("1.0.10" vs "1.0.9") serait plus
/// fragile qu'un simple entier croissant.
///
/// Best-effort : ne lève jamais d'exception (réseau coupé, JSON absent/malformé, endpoint non
/// déployé sur un backend plus ancien...) — retombe silencieusement sur "aucune mise à jour" plutôt
/// que de perturber la connexion normale au jeu.
Future<UpdateCheckResult> verifierMiseAJourDisponible(String serverBaseUrl) async {
  try {
    final base = serverBaseUrl.trim().replaceAll(RegExp(r'/+$'), '');
    if (base.isEmpty) return UpdateCheckResult.aucuneMiseAJour;

    final reponse = await http.get(Uri.parse('$base/apk_version.json')).timeout(const Duration(seconds: 4));
    if (reponse.statusCode != 200) return UpdateCheckResult.aucuneMiseAJour;

    final data = jsonDecode(reponse.body) as Map<String, dynamic>;
    final buildDistant = data['buildNumber'] as int?;
    final versionDistante = data['versionName'] as String?;
    if (buildDistant == null) return UpdateCheckResult.aucuneMiseAJour;

    final infoInstalle = await PackageInfo.fromPlatform();
    final buildInstalle = int.tryParse(infoInstalle.buildNumber) ?? 0;

    if (buildDistant > buildInstalle) {
      return UpdateCheckResult(disponible: true, versionDistante: versionDistante);
    }
    return UpdateCheckResult.aucuneMiseAJour;
  } catch (_) {
    return UpdateCheckResult.aucuneMiseAJour;
  }
}
