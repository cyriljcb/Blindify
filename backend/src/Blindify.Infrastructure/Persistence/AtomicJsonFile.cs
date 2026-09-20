using System.Text.Json;

namespace Blindify.Infrastructure.Persistence;

/// <summary>Écrit un fichier JSON de façon atomique (écrit dans un fichier temporaire adjacent puis
/// renomme) — évite qu'un crash/redémarrage pendant l'écriture ne laisse `stats.json`/`flags.json`
/// tronqué ou corrompu. Utilisé par StatsRepository et FlagsRepository, les deux seuls fichiers que le
/// backend écrit (jamais tracks.json/flags_resolutions.json — voir CLAUDE.md).</summary>
public static class AtomicJsonFile
{
    public static void Write<T>(string path, T data, JsonSerializerOptions options)
    {
        // Fichier temporaire à côté de la cible (même volume) : File.Move y est atomique côté OS,
        // contrairement à un chemin sur un volume différent qui retomberait sur une copie+suppression.
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(data, options));
        File.Move(tempPath, path, overwrite: true);
    }
}
