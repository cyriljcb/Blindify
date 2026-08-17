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
        round.Cible = Random.Shared.Next(2) == 0 ? RoundCible.Titre : RoundCible.Auteur;

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

        var estCorrecte = round.Mode switch
        {
            RoundMode.Qcm => reponse == track.Id,
            RoundMode.PremiereLettre => round.Cible == RoundCible.Titre
                ? EstPremiereLettreCorrecte(reponse, track.Title)
                : DecouperAuteurs(track.Artist).Any(auteur => EstPremiereLettreCorrecte(reponse, auteur)),
            _ => round.Cible == RoundCible.Titre
                ? answerMatcher.EstCorrecte(reponse, track.Title, session.Config.SeuilToleranceLevenshteinRatio)
                : DecouperAuteurs(track.Artist).Any(auteur => answerMatcher.EstCorrecte(reponse, auteur, session.Config.SeuilToleranceLevenshteinRatio)),
        };

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

    private bool EstPremiereLettreCorrecte(string reponse, string texteAttendu)
    {
        var normaliseeReponse = answerMatcher.Normaliser(reponse);
        var normaliseAttendu = answerMatcher.Normaliser(texteAttendu);
        return normaliseeReponse.Length > 0 && normaliseAttendu.Length > 0
               && normaliseeReponse[0] == normaliseAttendu[0];
    }

    /// <summary>Un morceau peut avoir plusieurs auteurs listés dans un seul champ ("A, B, C") —
    /// n'importe lequel d'entre eux est une réponse valable (voir retour utilisateur : exiger la
    /// liste complète est ingérable dès qu'il y a plus d'un featuring).</summary>
    private static IEnumerable<string> DecouperAuteurs(string artist) =>
        artist.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
