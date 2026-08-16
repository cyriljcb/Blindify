using Blindify.Application.Scoring;
using Blindify.Domain.Configuration;

namespace Blindify.Tests.Scoring;

public class BonusScoringServiceTests
{
    private readonly BonusScoringService _service = new();

    [Fact]
    public void PointsResultat_ReponseCorrecte_GagneLaMise()
    {
        Assert.Equal(50, _service.PointsResultat(mise: 50, estCorrecte: true));
    }

    [Fact]
    public void PointsResultat_ReponseIncorrecte_PerdLaMise()
    {
        Assert.Equal(-50, _service.PointsResultat(mise: 50, estCorrecte: false));
    }

    [Fact]
    public void ValeurPalier_RetourneLaValeurAuBonIndex()
    {
        var config = new SeriesConfig { PaliersDeMise = [10, 20, 30, 50] };

        Assert.Equal(30, _service.ValeurPalier(config, palierIndex: 2));
    }

    [Fact]
    public void PalierParDefautIndex_EstLePalierSafe()
    {
        Assert.Equal(0, _service.PalierParDefautIndex);
    }
}
