using Blindify.Domain.Enums;

namespace Blindify.Domain.Entities;

/// <summary>Question bonus de fin de série — voir architecture.md section 7. Les 4 paliers de mise
/// viennent de SeriesConfig.PaliersDeMise, pas dupliqués ici.</summary>
public class BonusRound
{
    public required string TrackId { get; set; }

    /// <summary>Toujours Titre, sauf morceau "disney" (Film) — pas de tirage Auteur ici, contrairement
    /// au round classique : la question bonus demande toujours le morceau lui-même, jamais un
    /// artiste crédité (voir BonusRoundService.CreerBonusRound).</summary>
    public RoundCible Cible { get; set; } = RoundCible.Titre;

    /// <summary>Tirée au hasard comme pour un round classique (retour utilisateur 2026-08-27 : la
    /// question bonus se limitait à la réponse tapée) — voir BonusRoundService.CreerBonusRound.</summary>
    public RoundMode Mode { get; set; } = RoundMode.TapeReponse;

    /// <summary>Les 4 IDs de morceaux proposés (Mode Qcm uniquement), générés à la création du bonus round — voir Round.QcmOptionTrackIds.</summary>
    public List<string>? QcmOptionTrackIds { get; set; }

    public DateTimeOffset? DebutPhaseMise { get; set; }
    public DateTimeOffset? DebutPhaseQuestion { get; set; }

    /// <summary>Pause écoulée pendant la phase actuellement active (remise à 0 au passage à la phase question).</summary>
    public long DureeEnPauseMs { get; set; }

    public List<BonusStake> Mises { get; set; } = [];
    public List<BonusAnswer> Reponses { get; set; } = [];
}
