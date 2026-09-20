namespace Blindify.Domain.Statistics;

/// <summary>Delta de confusion pour UN distracteur (V2) — combiné aux valeurs déjà accumulées dans
/// stats.json par StatsRepository.EnregistrerResultatsRound, jamais une valeur absolue.</summary>
public class ConfusionDelta
{
    public required string TrackId { get; init; }
    public int Presente { get; init; }
    public int Choisi { get; init; }
}

/// <summary>Résultat agrégé (Blindify.Application.Stats.RoundStatsAggregator) d'un round classique ou
/// d'une question bonus, prêt à être fusionné dans stats.json en une seule écriture atomique — vit
/// dans Domain (pas Application) pour rester référençable depuis Blindify.Infrastructure, qui ne
/// dépend jamais d'Application (voir la règle de dépendance des couches, CLAUDE.md/architecture.md).
/// Toujours un DELTA (à ajouter aux compteurs existants), jamais une valeur absolue.</summary>
public class RoundStatsUpdate
{
    public required string TrackId { get; init; }

    /// <summary>Clé "Mode:Cible" (ex. "Qcm:Titre"), préfixée "Bonus:" pour la question bonus (ex.
    /// "Bonus:TapeReponse:Auteur") — voir architecture.md section stats.json v2.</summary>
    public required string CleModeCible { get; init; }

    public int N { get; init; }
    public int Correct { get; init; }
    public int Absent { get; init; }
    public long TempsCorrectCumulMs { get; init; }

    /// <summary>Non-null uniquement pour la cible Année (V2, section 12.5).</summary>
    public long? EcartCumul { get; init; }

    public List<ConfusionDelta> Confusions { get; init; } = [];
}
