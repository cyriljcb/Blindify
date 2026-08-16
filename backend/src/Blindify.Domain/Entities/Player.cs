namespace Blindify.Domain.Entities;

/// <summary>
/// Identifié par PlayerId (stable, généré côté Flutter), jamais par ConnectionId
/// (mutable, réassocié à chaque reconnexion) — voir architecture.md section 5.
/// </summary>
public class Player
{
    public required string PlayerId { get; set; }
    public string? ConnectionId { get; set; }
    public required string Nom { get; set; }
    public int Score { get; set; }
    public string? TeamId { get; set; }
    public bool EstConnecte { get; set; }
}
