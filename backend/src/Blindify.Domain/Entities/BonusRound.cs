namespace Blindify.Domain.Entities;

/// <summary>Question bonus de fin de série — voir architecture.md section 7. Les 4 paliers de mise
/// viennent de SeriesConfig.PaliersDeMise, pas dupliqués ici.</summary>
public class BonusRound
{
    public required string TrackId { get; set; }
    public long DureeEnPauseMs { get; set; }
    public List<BonusStake> Mises { get; set; } = [];
    public List<BonusAnswer> Reponses { get; set; } = [];
}
