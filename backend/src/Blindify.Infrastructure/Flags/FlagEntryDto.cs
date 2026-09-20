using Blindify.Domain.Enums;

namespace Blindify.Infrastructure.Flags;

/// <summary>Une entrée de data/flags.json (V2, section 12.4) — le backend n'y ajoute jamais que des
/// entrées (jamais de suppression/modification), toujours via AtomicJsonFile. Cible/Mode/SerieTags sont
/// capturés au moment du signalement (pas relus depuis la session, qui peut avoir disparu au moment où
/// le pipeline data traite ce fichier).</summary>
public class FlagEntryDto
{
    public required string Id { get; set; }
    public required string TrackId { get; set; }
    public RaisonSignalement Raison { get; set; }
    public string? Commentaire { get; set; }

    /// <summary>"host" ou "admin" — qui a signalé, voir GameHub.SignalerMorceau.</summary>
    public required string Par { get; set; }

    public required string GameCode { get; set; }
    public List<string> SerieTags { get; set; } = [];
    public RoundCible Cible { get; set; }
    public RoundMode Mode { get; set; }
    public DateTimeOffset Horodatage { get; set; }
}
