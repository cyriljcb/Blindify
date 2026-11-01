import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart' show kIsWeb;

/// Port HTTP publié par le conteneur backend (docker-compose.yml, "5000:8080").
const portServeur = 5000;

/// Cherche le serveur Blindify sur le réseau local du téléphone — retour utilisateur (2026-11-01) :
/// `pi.local` n'est pas résolu sur certains Android (voir resolveMdnsHost) et l'IP DHCP du Pi
/// change d'un lieu à l'autre. Pas de broadcast UDP (il ne traverserait pas le mapping de port du
/// conteneur Docker, réseau bridge) : on interroge en parallèle les 254 adresses de chaque
/// sous-réseau /24 privé du téléphone sur GET /api/ping, et on garde la première qui répond
/// "blindify". Retourne l'URL du serveur (`http://<ip>:5000`), ou null si rien n'a répondu.
///
/// Jamais sur le web (dart:io indisponible) : la version web est servie par le Pi lui-même, son
/// adresse est déjà celle de la page.
Future<String?> chercherServeur({Duration delaiParAdresse = const Duration(milliseconds: 800)}) async {
  if (kIsWeb) return null;

  final prefixes = await _prefixesReseauLocal();
  if (prefixes.isEmpty) return null;

  final client = HttpClient()..connectionTimeout = delaiParAdresse;
  final trouve = Completer<String?>();
  try {
    final sondes = <Future<void>>[];
    for (final prefixe in prefixes) {
      for (var hote = 1; hote <= 254; hote++) {
        final ip = '$prefixe.$hote';
        sondes.add(_sonder(client, ip, delaiParAdresse).then((ok) {
          if (ok && !trouve.isCompleted) trouve.complete('http://$ip:$portServeur');
        }));
      }
    }
    unawaited(Future.wait(sondes).then((_) {
      if (!trouve.isCompleted) trouve.complete(null);
    }));
    return await trouve.future;
  } finally {
    client.close(force: true);
  }
}

/// Préfixes "a.b.c" des interfaces IPv4 privées (Wi-Fi, partage de connexion) — les interfaces
/// mobiles (adresses opérateur, souvent en 100.64/10 ou publiques) sont ignorées : le Pi n'y est
/// jamais joignable.
Future<List<String>> _prefixesReseauLocal() async {
  try {
    final interfaces = await NetworkInterface.list(type: InternetAddressType.IPv4, includeLoopback: false);
    final prefixes = <String>{};
    for (final interface in interfaces) {
      for (final adresse in interface.addresses) {
        final octets = adresse.address.split('.').map(int.parse).toList();
        final privee = octets[0] == 10 ||
            (octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31) ||
            (octets[0] == 192 && octets[1] == 168);
        if (privee) prefixes.add('${octets[0]}.${octets[1]}.${octets[2]}');
      }
    }
    return prefixes.toList();
  } catch (_) {
    return [];
  }
}

Future<bool> _sonder(HttpClient client, String ip, Duration delai) async {
  try {
    final requete = await client.getUrl(Uri.parse('http://$ip:$portServeur/api/ping')).timeout(delai);
    final reponse = await requete.close().timeout(delai);
    if (reponse.statusCode != 200) {
      await reponse.drain<void>();
      return false;
    }
    final corps = await reponse.transform(utf8.decoder).join().timeout(delai);
    final json = jsonDecode(corps);
    return json is Map && json['service'] == 'blindify';
  } catch (_) {
    return false;
  }
}
