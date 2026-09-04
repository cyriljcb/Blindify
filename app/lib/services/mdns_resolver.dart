import 'package:carp_multicast_lock/carp_multicast_lock.dart';
import 'package:multicast_dns/multicast_dns.dart';

/// Résout un hostname `.local` (mDNS/Bonjour) en adresse IP concrète avant connexion.
///
/// Android ne résout pas `.local` au niveau socket, contrairement à iOS/macOS/Windows
/// (voir docs/architecture.md "Nom local au lieu de l'IP") — sans ça, taper `pi.local`
/// dans l'app plante la connexion SignalR sur ce seul OS. Retourne [url] inchangée si
/// l'hôte n'est pas en `.local`, ou si la résolution échoue/n'aboutit pas à temps (auquel
/// cas [GameConnection.connect] échoue normalement avec son message d'erreur habituel).
///
/// `MulticastLock` (voir carp_multicast_lock) est indispensable sur Android : le
/// driver WiFi y filtre les paquets multicast entrants par défaut (économie de
/// batterie), donc sans ce verrou la réponse mDNS d'avahi est reçue par le téléphone
/// mais jetée avant d'atteindre le socket applicatif — le lookup expirait toujours en
/// silence malgré permissions et code par ailleurs corrects. No-op sur les autres OS.
Future<String> resolveMdnsHost(String url) async {
  final uri = Uri.tryParse(url);
  final host = uri?.host;
  if (uri == null || host == null || !host.toLowerCase().endsWith('.local')) {
    return url;
  }

  final lock = MulticastLock();
  final client = MDnsClient();
  try {
    await lock.acquireMulticastLock();
    await client.start();
    final lookup = client.lookup<IPAddressResourceRecord>(
      ResourceRecordQuery.addressIPv4(host),
      timeout: const Duration(seconds: 3),
    );
    await for (final record in lookup) {
      return uri.replace(host: record.address.address).toString();
    }
    return url;
  } catch (_) {
    return url;
  } finally {
    client.stop();
    await lock.releaseMulticastLock();
  }
}
