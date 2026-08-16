using Blindify.Domain.Configuration;

namespace Blindify.Application.Scoring;

public class ScoringService : IScoringService
{
    public int CalculerPointsEnJeu(DateTimeOffset debutRound, DateTimeOffset maintenant, long dureeEnPauseMs, SeriesConfig config)
    {
        var tempsEcouleMs = (maintenant - debutRound).TotalMilliseconds - dureeEnPauseMs;
        var ratio = Math.Clamp(tempsEcouleMs / config.DureeFenetreReponseMs, 0.0, 1.0);
        var points = config.PointsMax - ratio * (config.PointsMax - config.PointsMin);
        return (int)Math.Round(Math.Max(config.PointsMin, points));
    }

    public int PointsBonneReponse(int pointsEnJeu) => pointsEnJeu;

    public int PointsMauvaiseReponse(int pointsEnJeu, SeriesConfig config) =>
        -(int)Math.Round(pointsEnJeu * config.PenaliteMauvaiseReponseRatio);

    public int PointsAbsenceReponse(SeriesConfig config) => config.PenaliteAbsenceReponse;
}
