namespace Blindify.Domain.Entities;

public class RoundAnswer
{
    public required string PlayerId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public required string Reponse { get; set; }
    public bool EstCorrecte { get; set; }
    public int Points { get; set; }

    /// <summary>pointsEnJeu au moment de la réponse (magnitude, indépendante du signe) — permet de
    /// recalculer Points si ValidateAnswerManually change EstCorrecte après coup.</summary>
    public int PointsEnJeu { get; set; }

    /// <summary>Entrée synthétique ajoutée par RoundService.TerminerParTimeout (le joueur n'a pas
    /// répondu) — distingue une vraie réponse texte vide (improbable côté UI, mais pas impossible)
    /// d'une absence, pour le calcul des statistiques de réponse (V2, StatsRepository).</summary>
    public bool EstAbsent { get; set; }

    /// <summary>TrackId de l'option choisie en mode Qcm uniquement (V2) — permet de recouper avec
    /// Round.Options pour le calcul des confusions (data/scripts/suggest_traps.py). Null dans les
    /// autres modes (TapeReponse/PremiereLettre, où il n'y a pas d'"option" au sens QCM).</summary>
    public string? OptionChoisieTrackId { get; set; }

    /// <summary>Vrai si l'option choisie (voir OptionChoisieTrackId) avait son texte substitué par
    /// une feinte — voir RoundOption.EstFeinte.</summary>
    public bool OptionChoisieEstFeinte { get; set; }

    /// <summary>Vrai si l'option choisie (voir OptionChoisieTrackId) était un piège réel (trapWith)
    /// — voir RoundOption.EstPiege.</summary>
    public bool OptionChoisieEstPiege { get; set; }

    /// <summary>Temps de réponse réel (hors durée de pause), en ms — indépendant de PointsEnJeu qui
    /// n'en est qu'une transformation par la formule de scoring (V2, statistiques de réponse).</summary>
    public int TempsReponseMs { get; set; }

    /// <summary>Écart (en années) entre la réponse et l'année réelle — cible Année uniquement (V2,
    /// section 12.5). Null pour toute autre cible, ou si la réponse n'était pas un nombre valide.</summary>
    public int? EcartAnnee { get; set; }
}
