import 'dart:async';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;
import 'package:open_filex/open_filex.dart';
import 'package:path_provider/path_provider.dart';
import 'package:url_launcher/url_launcher.dart';

import 'mdns_resolver.dart';

/// Mise à jour de l'app depuis l'APK servi par le backend (voir docs/architecture.md "Mettre à
/// jour l'app Android sans câble").
///
/// Retour utilisateur (2026-10-17) : passer par le navigateur empilait les APK dans
/// « Téléchargements » (blindify.apk, blindify (1).apk…). L'app télécharge maintenant elle-même
/// l'APK dans son cache privé, toujours sous le même nom (chaque version écrase la précédente),
/// puis ouvre directement l'installeur Android (open_filex, via son FileProvider). Le fichier est
/// supprimé au démarrage suivant ([nettoyerApkTelecharge]). Android impose toujours la
/// confirmation « Mettre à jour », et la première fois l'autorisation « installer des applis
/// inconnues » pour Blindify (permission REQUEST_INSTALL_PACKAGES du manifeste).
///
/// En cas d'échec (réseau, installeur introuvable…), repli sur l'ancien fonctionnement : le
/// navigateur télécharge l'APK.
///
/// Retour utilisateur (2026-09-27) : le nom `.local` (ex. pi.local) est d'abord résolu en IP —
/// Android ne résout pas le mDNS. Le numéro de build est ajouté à l'URL pour qu'aucun cache ne
/// resserve l'ancien APK.
class MiseAJourApk {
  MiseAJourApk._();

  /// Avancement du téléchargement en cours : null si aucun, 0..1 sinon (0 tant que la taille est
  /// inconnue).
  static final progression = ValueNotifier<double?>(null);

  static const _nomDossier = 'mise_a_jour';
  static const _nomFichier = 'blindify.apk';
  static const _typeApk = 'application/vnd.android.package-archive';
}

Future<void> ouvrirMiseAJourApk(String baseUrl, {String? version}) async {
  final base = (await resolveMdnsHost(baseUrl.trim())).replaceAll(RegExp(r'/+$'), '');
  if (base.isEmpty) return;
  final suffixe = version == null ? '' : '?v=${Uri.encodeQueryComponent(version)}';
  final url = Uri.parse('$base/blindify.apk$suffixe');

  if (kIsWeb || !Platform.isAndroid) {
    await launchUrl(url, mode: LaunchMode.externalApplication);
    return;
  }
  if (MiseAJourApk.progression.value != null) return; // téléchargement déjà en cours

  try {
    final apk = await _telecharger(url);
    final resultat = await OpenFilex.open(apk.path, type: MiseAJourApk._typeApk);
    if (resultat.type != ResultType.done) throw Exception(resultat.message);
  } catch (e) {
    debugPrint('Mise à jour intégrée impossible ($e) — repli sur le navigateur');
    await launchUrl(url, mode: LaunchMode.externalApplication);
  } finally {
    MiseAJourApk.progression.value = null;
  }
}

Future<File> _telecharger(Uri url) async {
  MiseAJourApk.progression.value = 0;
  final dossier = Directory('${(await getTemporaryDirectory()).path}/${MiseAJourApk._nomDossier}');
  await dossier.create(recursive: true);
  final partiel = File('${dossier.path}/${MiseAJourApk._nomFichier}.part');
  final client = http.Client();
  try {
    final reponse = await client.send(http.Request('GET', url)).timeout(const Duration(seconds: 20));
    if (reponse.statusCode != 200) throw HttpException('HTTP ${reponse.statusCode}', uri: url);

    final total = reponse.contentLength ?? 0;
    var recu = 0;
    final sortie = partiel.openWrite();
    try {
      // Délai d'inactivité plutôt que délai global : l'APK pèse ~70 Mo, la durée totale dépend du WiFi.
      await for (final morceau in reponse.stream.timeout(const Duration(seconds: 30))) {
        sortie.add(morceau);
        recu += morceau.length;
        if (total > 0) MiseAJourApk.progression.value = recu / total;
      }
    } finally {
      await sortie.close();
    }
    if (total > 0 && recu != total) throw HttpException('Téléchargement incomplet ($recu/$total octets)', uri: url);
    return await partiel.rename('${dossier.path}/${MiseAJourApk._nomFichier}');
  } finally {
    client.close();
  }
}

/// Supprime l'APK téléchargé lors d'une mise à jour précédente — appelé au démarrage de l'app.
Future<void> nettoyerApkTelecharge() async {
  if (kIsWeb || !Platform.isAndroid) return;
  try {
    final dossier = Directory('${(await getTemporaryDirectory()).path}/${MiseAJourApk._nomDossier}');
    if (await dossier.exists()) await dossier.delete(recursive: true);
  } catch (e) {
    debugPrint('Nettoyage de l\'APK téléchargé impossible : $e');
  }
}
