namespace Blindify.Infrastructure.Stats;

/// <summary>
/// Compteurs runtime (playCount), persistés dans stats.json — séparé de tracks.json pour ne jamais
/// entrer en collision avec le script d'import CSV — voir architecture.md section 4.
/// </summary>
public interface IStatsRepository
{
    int GetPlayCount(string trackId);
    void IncrementPlayCount(string trackId);
}
