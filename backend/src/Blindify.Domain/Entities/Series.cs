using Blindify.Domain.Configuration;

namespace Blindify.Domain.Entities;

public class Series
{
    public int Index { get; set; }
    public required SeriesConfig Config { get; set; }
    public List<Round> Rounds { get; set; } = [];
    public BonusRound? BonusRound { get; set; }
}
