using Blindify.Domain.Enums;

namespace Blindify.Domain.Entities;

/// <summary>Question bonus de fin de série — voir architecture.md section 7. Les 4 paliers de mise
/// viennent de SeriesConfig.PaliersDeMise, pas dupliqués ici.</summary>
public class BonusRound
{
    /// <summary>Voir Round.Id — même rôle pour SelectStake/SubmitBonusAnswer.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string TrackId { get; set; }

    /// <summary>Tirée via RoundService.ChoisirCible, la même logique pondérée que pour un round
    /// classique (Titre/Auteur/Année, forcée Film pour "disney") — voir BonusRoundService.CreerBonusRound.
    /// Le défaut Titre ci-dessous n'est qu'une valeur d'initialisation, jamais la valeur réelle en jeu.</summary>
    public RoundCible Cible { get; set; } = RoundCible.Titre;

    /// <summary>Tirée au hasard comme pour un round classique (retour utilisateur 2026-08-27 : la
    /// question bonus se limitait à la réponse tapée) — voir BonusRoundService.CreerBonusRound.</summary>
    public RoundMode Mode { get; set; } = RoundMode.TapeReponse;

    /// <summary>Les 4 IDs de morceaux proposés (Mode Qcm uniquement), générés à la création du bonus round — voir Round.QcmOptionTrackIds.</summary>
    public List<string>? QcmOptionTrackIds { get; set; }

    /// <summary>Voir Round.Options — construit une seule fois au démarrage de la phase question
    /// (BonusTimerCoordinator.DiffuserDebutPhaseQuestionAsync), pas à la création du BonusRound
    /// (les feintes ne sont appliquées qu'à la diffusion, voir ce même commentaire historique
    /// plus bas dans ce fichier).</summary>
    public List<RoundOption>? Options { get; set; }

    /// <summary>Voir Round.AnneeOptions — même construction, au démarrage de la phase question.</summary>
    public List<int>? AnneeOptions { get; set; }

    /// <summary>"Course" (Mode Qcm uniquement, voir GameConfig.ProbabiliteBonusCourse) : le premier
    /// joueur à répondre — juste ou faux — décide seul du sort de sa mise ; tant qu'aucune réponse
    /// n'est enregistrée, personne n'est affecté. Toujours false hors Mode.Qcm.</summary>
    public bool EstCourse { get; set; }

    public DateTimeOffset? DebutPhaseMise { get; set; }
    public DateTimeOffset? DebutPhaseQuestion { get; set; }

    /// <summary>Pause écoulée pendant la phase actuellement active (remise à 0 au passage à la phase question).</summary>
    public long DureeEnPauseMs { get; set; }

    public List<BonusStake> Mises { get; set; } = [];
    public List<BonusAnswer> Reponses { get; set; } = [];
}
