using Blindify.Domain.Configuration;

namespace Blindify.Application.Scoring;

public class BonusScoringService : IBonusScoringService
{
    public int PalierParDefautIndex => 0;

    public int ValeurPalier(SeriesConfig config, int palierIndex) => config.PaliersDeMise[palierIndex];

    public int PointsResultat(int mise, bool estCorrecte) => estCorrecte ? mise : -mise;
}
