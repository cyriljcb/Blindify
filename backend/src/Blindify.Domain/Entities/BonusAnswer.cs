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

    /// <summary>Voir RoundAnswer.EstAbsent — même rôle, posé par BonusRoundService.TerminerParTimeout.</summary>
    public bool EstAbsent { get; set; }

    /// <summary>Voir RoundAnswer.OptionChoisieTrackId.</summary>
    public string? OptionChoisieTrackId { get; set; }

    /// <summary>Voir RoundAnswer.OptionChoisieEstFeinte.</summary>
    public bool OptionChoisieEstFeinte { get; set; }

    /// <summary>Voir RoundAnswer.OptionChoisieEstPiege.</summary>
    public bool OptionChoisieEstPiege { get; set; }

    /// <summary>Voir RoundAnswer.TempsReponseMs.</summary>
    public int TempsReponseMs { get; set; }

    /// <summary>Voir RoundAnswer.EcartAnnee.</summary>
    public int? EcartAnnee { get; set; }
}
