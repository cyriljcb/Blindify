using Blindify.Domain.Enums;

namespace Blindify.Domain.Entities;

/// <summary>Round classique — voir architecture.md section 6.</summary>
public class Round
{
    public required string TrackId { get; set; }
    public RoundMode Mode { get; set; }
    public DateTimeOffset? DebutRound { get; set; }
    public long DureeEnPauseMs { get; set; }
    public List<RoundAnswer> Reponses { get; set; } = [];
}
