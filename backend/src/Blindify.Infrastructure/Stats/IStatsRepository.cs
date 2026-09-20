using Blindify.Domain.Statistics;

namespace Blindify.Infrastructure.Stats;

/// <summary>
/// Compteurs runtime (playCount), persistés dans stats.json — séparé de tracks.json pour ne jamais
/// entrer en collision avec le script d'import CSV — voir architecture.md section 4.
/// </summary>
public interface IStatsRepository
{
    int GetPlayCount(string trackId);
    void IncrementPlayCount(string trackId);

    /// <summary>Fusionne (ajoute aux compteurs déjà accumulés, jamais un remplacement) le résultat
    /// d'un round classique ou d'une question bonus (V2, socle statistiques de réponse) — voir
    /// Blindify.Application.Stats.RoundStatsAggregator. Une seule écriture atomique par round, jamais
    /// combinée avec IncrementPlayCount (compteur distinct, déjà écrit au démarrage du round).</summary>
    void EnregistrerResultatsRound(RoundStatsUpdate update);
}
