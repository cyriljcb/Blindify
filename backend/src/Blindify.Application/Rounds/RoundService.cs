using Blindify.Application.Answers;
using Blindify.Application.Qcm;
using Blindify.Application.Scoring;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;
using Blindify.Domain.Enums;

namespace Blindify.Application.Rounds;

public class RoundService(IScoringService scoring, IQcmGenerator qcmGenerator, IAnswerMatcher answerMatcher) : IRoundService
{
    public List<Track> SelectionnerMorceaux(IReadOnlyList<Track> pool, IReadOnlyList<string> tags, int nombre, HashSet<string> dejaUtilises)
    {
        var candidats = FiltrerParTagsOuGenres(pool, tags)
            .Where(t => !dejaUtilises.Contains(t.Id))
            .ToList();

        if (candidats.Count < nombre)
            candidats = pool.Where(t => !dejaUtilises.Contains(t.Id)).ToList();

        var resultat = new List<Track>();
        var disponibles = new List<Track>(candidats);

        while (resultat.Count < nombre && disponibles.Count > 0)
        {
            var index = Random.Shared.Next(disponibles.Count);
            var track = disponibles[index];
            resultat.Add(track);
            dejaUtilises.Add(track.Id);
            disponibles.RemoveAt(index);
        }

        return resultat;
    }

    public void DemarrerRound(Round round, Track track, IReadOnlyList<Track> catalogueComplet, GameConfig config, DateTimeOffset maintenant)
    {
        round.DebutRound = maintenant;

        if (round.Mode == RoundMode.Qcm)
        {
            var options = qcmGenerator.GenererOptions(track, catalogueComplet, config, Random.Shared);
            round.QcmOptionTrackIds = options.OptionsTrackIds.ToList();
        }
    }

    public RoundAnswer? SoumettreReponse(GameSession session, Round round, SeriesConfig seriesConfig, Track track, string playerId, string reponse, DateTimeOffset maintenant)
    {
        if (session.EnPause) return null;
        if (round.DebutRound is null) return null;
        if (round.Reponses.Any(r => r.PlayerId == playerId)) return null;

        var estCorrecte = round.Mode == RoundMode.Qcm
            ? reponse == track.Id
            : answerMatcher.EstCorrecte(reponse, track.Title, session.Config.SeuilToleranceLevenshteinRatio);

        var pointsEnJeu = scoring.CalculerPointsEnJeu(round.DebutRound.Value, maintenant, round.DureeEnPauseMs, seriesConfig);
        var points = estCorrecte
            ? scoring.PointsBonneReponse(pointsEnJeu)
            : scoring.PointsMauvaiseReponse(pointsEnJeu, seriesConfig);

        var answer = new RoundAnswer
        {
            PlayerId = playerId,
            Timestamp = maintenant,
            Reponse = reponse,
            EstCorrecte = estCorrecte,
            Points = points,
            PointsEnJeu = pointsEnJeu
        };

        round.Reponses.Add(answer);
        AppliquerPoints(session, playerId, points);

        return answer;
    }

    public void TerminerParTimeout(GameSession session, Round round, SeriesConfig config)
    {
        var repondants = round.Reponses.Select(r => r.PlayerId).ToHashSet();
        var penalite = scoring.PointsAbsenceReponse(config);

        foreach (var player in session.Players.Where(p => !repondants.Contains(p.PlayerId)))
        {
            round.Reponses.Add(new RoundAnswer
            {
                PlayerId = player.PlayerId,
                Timestamp = DateTimeOffset.UtcNow,
                Reponse = "",
                EstCorrecte = false,
                Points = penalite,
                PointsEnJeu = 0
            });

            AppliquerPoints(session, player.PlayerId, penalite);
        }
    }

    public RoundAnswer? ValiderManuellement(GameSession session, Round round, SeriesConfig config, string playerId, bool estCorrecte)
    {
        var answer = round.Reponses.FirstOrDefault(r => r.PlayerId == playerId);
        if (answer is null) return null;

        var nouveauxPoints = estCorrecte
            ? scoring.PointsBonneReponse(answer.PointsEnJeu)
            : scoring.PointsMauvaiseReponse(answer.PointsEnJeu, config);

        AppliquerPoints(session, playerId, nouveauxPoints - answer.Points);
        answer.EstCorrecte = estCorrecte;
        answer.Points = nouveauxPoints;

        return answer;
    }

    private static IEnumerable<Track> FiltrerParTagsOuGenres(IReadOnlyList<Track> pool, IReadOnlyList<string> tags) =>
        tags.Count == 0 ? pool : pool.Where(t => t.Tags.Intersect(tags).Any() || t.Genres.Intersect(tags).Any());

    private static void AppliquerPoints(GameSession session, string playerId, int points)
    {
        var player = session.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (player is not null) player.Score += points;
    }
}
