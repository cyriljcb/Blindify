namespace Blindify.Infrastructure.Stats;

/// <summary>Miroir d'une entrée stats.json v2 — clé = TrackId du distracteur confondu avec le morceau
/// correct (V2, section 12.1/12.3). Voir Blindify.Domain.Statistics.ConfusionDelta.</summary>
public class ConfusionStatDto
{
    public int Presente { get; set; }
    public int Choisi { get; set; }
}
