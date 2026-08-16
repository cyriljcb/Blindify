namespace Blindify.Domain.Entities;

/// <summary>Réponse d'un joueur à la phase 2 de la question bonus.</summary>
public class BonusAnswer
{
    public required string PlayerId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public required string Reponse { get; set; }
    public bool EstCorrecte { get; set; }

    /// <summary>+mise si EstCorrecte, -mise sinon — voir architecture.md section 7.</summary>
    public int Points { get; set; }
}
