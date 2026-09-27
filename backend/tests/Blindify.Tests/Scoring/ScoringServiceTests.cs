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

    // ----- V2, section 12.5 : cible Année -----

    [Fact]
    public void PointsAnneeApproximative_EcartNul_RetournePleinPointsEnJeu()
    {
        Assert.Equal(100, _service.PointsAnneeApproximative(pointsEnJeu: 100, ecartAnnee: 0, Config()));
    }

    [Theory]
    [InlineData(1, 75)]  // round(100 * (1 - 1/4))
    [InlineData(2, 50)]  // round(100 * (1 - 2/4))
    [InlineData(3, 25)]  // round(100 * (1 - 3/4))
    public void PointsAnneeApproximative_EcartDansLaTolerance_DegressifLineaire(int ecart, int pointsAttendus)
    {
        Assert.Equal(pointsAttendus, _service.PointsAnneeApproximative(pointsEnJeu: 100, ecartAnnee: ecart, Config()));
    }

    // Retour utilisateur (2026-09-27) : au-delà de la tolérance, la pénalité grandit avec l'écart
    // (ToleranceAnnee=3, EcartAnneePenaliteMax=12, PenaliteAnneeMaxRatio=1.0 par défaut) — réponse
    // attendue 2000 : 1995 coûte 17, 1990 coûte 58, 1985 et avant coûtent 100.
    [Theory]
    [InlineData(4, -8)]    // round(100 * 1/12)
    [InlineData(5, -17)]   // round(100 * 2/12)
    [InlineData(10, -58)]  // round(100 * 7/12)
    [InlineData(15, -100)] // plafond atteint
    [InlineData(20, -100)] // plafonné
    public void PointsAnneeApproximative_EcartAuDelaDeLaTolerance_PenaliteProportionnelleALEcart(int ecart, int pointsAttendus)
    {
        Assert.Equal(pointsAttendus, _service.PointsAnneeApproximative(pointsEnJeu: 100, ecartAnnee: ecart, Config()));
    }

    [Fact]
    public void PointsAnneeApproximative_PenaliteSuitLesPointsEnJeu()
    {
        // Réponse lente (pointsEnJeu=40) : même proportion, sur une base plus petite.
        Assert.Equal(-40, _service.PointsAnneeApproximative(pointsEnJeu: 40, ecartAnnee: 30, Config()));
    }
}
