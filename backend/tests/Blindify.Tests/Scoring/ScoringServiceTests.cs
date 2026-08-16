using Blindify.Application.Scoring;
using Blindify.Domain.Configuration;

namespace Blindify.Tests.Scoring;

public class ScoringServiceTests
{
    private readonly ScoringService _service = new();

    private static SeriesConfig Config() => new()
    {
        DureeFenetreReponseMs = 10_000,
        PointsMax = 100,
        PointsMin = 20,
        PenaliteMauvaiseReponseRatio = 0.5,
        PenaliteAbsenceReponse = -5
    };

    [Fact]
    public void CalculerPointsEnJeu_AuDebutDuRound_RetournePointsMax()
    {
        var debut = DateTimeOffset.UtcNow;
        var points = _service.CalculerPointsEnJeu(debut, debut, dureeEnPauseMs: 0, Config());

        Assert.Equal(100, points);
    }

    [Fact]
    public void CalculerPointsEnJeu_ALaFinDeLaFenetre_RetournePointsMin()
    {
        var debut = DateTimeOffset.UtcNow;
        var maintenant = debut.AddMilliseconds(10_000);
        var points = _service.CalculerPointsEnJeu(debut, maintenant, dureeEnPauseMs: 0, Config());

        Assert.Equal(20, points);
    }

    [Fact]
    public void CalculerPointsEnJeu_AuDelaDeLaFenetre_RestePlafonneAPointsMin()
    {
        var debut = DateTimeOffset.UtcNow;
        var maintenant = debut.AddMilliseconds(30_000);
        var points = _service.CalculerPointsEnJeu(debut, maintenant, dureeEnPauseMs: 0, Config());

        Assert.Equal(20, points);
    }

    [Fact]
    public void CalculerPointsEnJeu_DureeEnPause_NeutraliseLeTempsEcoule()
    {
        var debut = DateTimeOffset.UtcNow;
        // 8s réelles écoulées, mais 5s de pause : temps utile = 3s sur une fenêtre de 10s.
        var maintenant = debut.AddMilliseconds(8_000);
        var points = _service.CalculerPointsEnJeu(debut, maintenant, dureeEnPauseMs: 5_000, Config());

        // ratio = 3000/10000 = 0.3 → 100 - 0.3*(100-20) = 76
        Assert.Equal(76, points);
    }

    [Fact]
    public void PointsMauvaiseReponse_EstLaMoitieDuGainEnValeurAbsolue()
    {
        var points = _service.PointsMauvaiseReponse(pointsEnJeu: 80, Config());

        Assert.Equal(-40, points);
    }

    [Fact]
    public void PointsAbsenceReponse_EstLaPenaliteFixeConfiguree()
    {
        var points = _service.PointsAbsenceReponse(Config());

        Assert.Equal(-5, points);
    }
}
