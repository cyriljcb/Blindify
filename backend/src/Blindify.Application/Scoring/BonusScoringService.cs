using Blindify.Domain.Configuration;

namespace Blindify.Application.Scoring;

public class BonusScoringService : IBonusScoringService
{
    public int PalierParDefautIndex => 0;

    /// <summary>Index hors bornes ramené au palier par défaut plutôt qu'une exception — filet de
    /// sécurité derrière la validation de BonusRoundService.EnregistrerMise (audit 2026-11-10).</summary>
    public int ValeurPalier(SeriesConfig config, int palierIndex) =>
        config.PaliersDeMise.Length == 0 ? 0
        : palierIndex >= 0 && palierIndex < config.PaliersDeMise.Length ? config.PaliersDeMise[palierIndex]
        : config.PaliersDeMise[Math.Min(PalierParDefautIndex, config.PaliersDeMise.Length - 1)];

    public int PointsResultat(int mise, bool estCorrecte) => estCorrecte ? mise : -mise;
}
