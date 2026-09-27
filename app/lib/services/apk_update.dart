import 'package:url_launcher/url_launcher.dart';

import 'mdns_resolver.dart';

/// Ouvre l'APK servi par le backend (voir docs/architecture.md "Mettre à jour l'app Android
/// sans câble") dans le navigateur — téléchargement puis installation par-dessus (signature
/// debug stable d'un build à l'autre sur le poste qui build).
///
/// Retour utilisateur (2026-09-27) : le nom `.local` (ex. pi.local) est d'abord résolu en IP — le
/// navigateur Android ne résout pas le mDNS, le téléchargement échouait donc avec l'adresse telle
/// que saisie. Le numéro de build est ajouté à l'URL pour qu'aucun cache ne resserve l'ancien APK.
Future<void> ouvrirMiseAJourApk(String baseUrl, {String? version}) async {
  final base = (await resolveMdnsHost(baseUrl.trim())).replaceAll(RegExp(r'/+$'), '');
  if (base.isEmpty) return;
  final suffixe = version == null ? '' : '?v=${Uri.encodeQueryComponent(version)}';
  await launchUrl(Uri.parse('$base/blindify.apk$suffixe'), mode: LaunchMode.externalApplication);
}
