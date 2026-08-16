using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;

namespace Blindify.Application.Bonus;

/// <summary>Cycle de vie de la question bonus (mise à l'aveugle puis question) — voir architecture.md section 7.</summary>
public interface IBonusRoundService
{
    BonusRound CreerBonusRound(Track track);

    void DemarrerPhaseMise(BonusRound bonusRound, DateTimeOffset maintenant);

    /// <summary>Retourne false si la mise est invalide (phase question déjà démarrée, ou joueur a déjà misé).</summary>
    bool EnregistrerMise(BonusRound bonusRound, string playerId, int palierIndex);

    /// <summary>Applique le palier "safe" par défaut à tout joueur n'ayant pas misé dans le délai.</summary>
    void AppliquerPaliersParDefaut(GameSession session, BonusRound bonusRound);

    void DemarrerPhaseQuestion(BonusRound bonusRound, DateTimeOffset maintenant);

    /// <summary>
    /// Soumet la réponse d'un joueur (correspondance texte, comme en mode TapeReponse). Retourne null si
    /// invalide (partie en pause, phase question pas démarrée, joueur a déjà répondu ou n'a pas misé).
    /// </summary>
    BonusAnswer? SoumettreReponse(GameSession session, BonusRound bonusRound, SeriesConfig config, Track track, string playerId, string reponse, DateTimeOffset maintenant);

    /// <summary>Absence de réponse en fin de phase question → traitée comme une réponse fausse (perte de la mise).</summary>
    void TerminerParTimeout(GameSession session, BonusRound bonusRound, SeriesConfig config);
}
