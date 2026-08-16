namespace Blindify.Domain.Entities;

/// <summary>Palier de mise choisi par un joueur (phase 1 de la question bonus).</summary>
public class BonusStake
{
    public required string PlayerId { get; set; }

    /// <summary>Index dans SeriesConfig.PaliersDeMise.</summary>
    public int PalierIndex { get; set; }
}
