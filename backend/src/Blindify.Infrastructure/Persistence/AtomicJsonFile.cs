using System.Text.Json;

namespace Blindify.Infrastructure.Persistence;

/// <summary>Écrit un fichier JSON en limitant la fenêtre de corruption en cas de crash/redémarrage
/// pendant l'écriture : sérialise dans un fichier temporaire adjacent (écriture complète, jamais vue
/// tronquée par un lecteur) puis recopie son contenu sur la cible. Utilisé par StatsRepository et
/// FlagsRepository, les deux seuls fichiers que le backend écrit (jamais tracks.json/
/// flags_resolutions.json — voir CLAUDE.md).
///
/// PAS un `File.Move` (rename) : `stats.json`/`flags.json` sont montés individuellement en volume
/// Docker (docker-compose.yml, un bind mount PAR FICHIER, pas un mount du dossier `data/` entier) —
/// remplacer l'inode d'un point de montage par rename() échoue avec "Device or resource busy" (EBUSY),
/// confirmé en conditions réelles sur le Pi (incident du 2026-09-20 : `StartRound` plantait derrière
/// `IncrementPlayCount`). `File.Copy` réécrit le contenu de l'inode existant en place (comme le
/// `File.WriteAllText` direct d'avant l'écriture "atomique"), ce qui reste compatible avec un bind
/// mount fichier — la fenêtre de corruption n'est plus nulle (le temps de la recopie), mais bien plus
/// courte qu'une sérialisation directe sur la cible, et le fichier temporaire garantit que le contenu
/// à recopier est déjà complet et valide avant d'y toucher.</summary>
public static class AtomicJsonFile
{
    public static void Write<T>(string path, T data, JsonSerializerOptions options)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(data, options));
        File.Copy(tempPath, path, overwrite: true);
        File.Delete(tempPath);
    }
}
