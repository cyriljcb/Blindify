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

    [Fact]
    public void EstPenaliteAbsenceEquitable_DefautV2_EstAcceptee()
    {
        // ratio=0.5, PointsMin=20 -> seuil = -(0.75*0.5-0.25)*20 = -2.5 ; -2 > -2.5 -> équitable.
        Assert.True(_service.EstPenaliteAbsenceEquitable(penaliteAbsenceReponse: -2, penaliteMauvaiseReponseRatio: 0.5, pointsMin: 20));
    }

    [Fact]
    public void EstPenaliteAbsenceEquitable_AncienneValeurV1_EstRejetee()
    {
        // -5 <= -2.5 : rendait un clic au hasard sur un QCM plus rentable que l'abstention.
        Assert.False(_service.EstPenaliteAbsenceEquitable(penaliteAbsenceReponse: -5, penaliteMauvaiseReponseRatio: 0.5, pointsMin: 20));
    }

    [Fact]
    public void EstPenaliteAbsenceEquitable_PenaliteNulle_EstToujoursAcceptee()
    {
        Assert.True(_service.EstPenaliteAbsenceEquitable(penaliteAbsenceReponse: 0, penaliteMauvaiseReponseRatio: 0.5, pointsMin: 20));
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

    [Fact]
    public void PointsAnneeApproximative_EcartAuDelaDeLaTolerance_PenaliteHabituelle()
    {
        // ToleranceAnnee=3 (défaut) : écart 4 -> pénalité classique, pas de dégressivité.
        Assert.Equal(-50, _service.PointsAnneeApproximative(pointsEnJeu: 100, ecartAnnee: 4, Config()));
    }
}
