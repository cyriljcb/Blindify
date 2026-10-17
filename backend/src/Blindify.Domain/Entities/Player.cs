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

    /// <summary>Joker (V2, section 12.7) — un seul par joueur et par partie complète, remis à false par
    /// GameHub.RejouerPartie.</summary>
    public bool JokerUtilise { get; set; }

    /// <summary>Retour utilisateur (2026-10-17) — le joueur a quitté volontairement la partie en cours
    /// (GameHub.QuitterPartie). Reste dans Players pour garder son score au classement, mais n'est plus
    /// compté comme absent (ni pénalité d'absence, ni palier bonus par défaut). Remis à false s'il
    /// rejoint à nouveau avec le même PlayerId (JoinGame) — score conservé.</summary>
    public bool APartie { get; set; }
}
