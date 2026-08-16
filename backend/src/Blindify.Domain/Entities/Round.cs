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

    /// <summary>Les 4 IDs de morceaux proposés (mode Qcm uniquement), générés au démarrage du round.</summary>
    public List<string>? QcmOptionTrackIds { get; set; }
}
