using Blindify.Application.Answers;
using Blindify.Application.Rounds;
using Blindify.Application.Scoring;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;
using Blindify.Domain.Enums;

namespace Blindify.Application.Bonus;

public class BonusRoundService(IBonusScoringService bonusScoring, IAnswerMatcher answerMatcher) : IBonusRoundService
{
    // Cible forcée à Film pour les morceaux "disney" (même raison que RoundService.DemarrerRound) :
    // ni le titre réel de la chanson ni l'artiste crédité ne sont devinables pour ce type de contenu.
    // Sinon, même tirage 50/50 Titre/Auteur que RoundService.DemarrerRound (retour utilisateur :
    // la question bonus tombait toujours sur le titre, jamais l'artiste).
    public BonusRound CreerBonusRound(Track track) => new()
    {
        TrackId = track.Id,
        Cible = track.Tags.Contains("disney", StringComparer.OrdinalIgnoreCase)
            ? RoundCible.Film
            : Random.Shared.Next(2) == 0 && TitreVariantes.EstEligibleCommeCible(track.Title) ? RoundCible.Titre : RoundCible.Auteur
    };

    public void DemarrerPhaseMise(BonusRound bonusRound, DateTimeOffset maintenant) => bonusRound.DebutPhaseMise = maintenant;

    public bool EnregistrerMise(GameSession session, BonusRound bonusRound, string playerId, int palierIndex)
    {
        if (session.EnPause) return false;
        if (bonusRound.DebutPhaseQuestion is not null) return false;
        if (bonusRound.Mises.Any(m => m.PlayerId == playerId)) return false;

        bonusRound.Mises.Add(new BonusStake { PlayerId = playerId, PalierIndex = palierIndex });
        return true;
    }

    public void AppliquerPaliersParDefaut(GameSession session, BonusRound bonusRound)
    {
        var ontMise = bonusRound.Mises.Select(m => m.PlayerId).ToHashSet();

        foreach (var player in session.Players.Where(p => !ontMise.Contains(p.PlayerId)))
            bonusRound.Mises.Add(new BonusStake { PlayerId = player.PlayerId, PalierIndex = bonusScoring.PalierParDefautIndex });
    }

    public void DemarrerPhaseQuestion(BonusRound bonusRound, DateTimeOffset maintenant)
    {
        bonusRound.DebutPhaseQuestion = maintenant;
        bonusRound.DureeEnPauseMs = 0;
    }

    public BonusAnswer? SoumettreReponse(GameSession session, BonusRound bonusRound, SeriesConfig config, Track track, string playerId, string reponse, DateTimeOffset maintenant)
    {
        if (session.EnPause) return null;
        if (bonusRound.DebutPhaseQuestion is null) return null;
        if (bonusRound.Reponses.Any(r => r.PlayerId == playerId)) return null;

        var mise = bonusRound.Mises.FirstOrDefault(m => m.PlayerId == playerId);
        if (mise is null) return null;

        var reponsesAcceptables = bonusRound.Cible switch
        {
            RoundCible.Film => [FilmNameResolver.Resoudre(track)],
            RoundCible.Auteur => AuteurVariantes.Acceptables(track.Artist),
            _ => TitreVariantes.Acceptables(track.Title)
        };
        var estCorrecte = reponsesAcceptables.Any(texte => answerMatcher.EstCorrecte(reponse, texte, session.Config.SeuilToleranceLevenshteinRatio));
        var valeurMise = bonusScoring.ValeurPalier(config, mise.PalierIndex);
        var points = bonusScoring.PointsResultat(valeurMise, estCorrecte);

        var answer = new BonusAnswer
        {
            PlayerId = playerId,
            Timestamp = maintenant,
            Reponse = reponse,
            EstCorrecte = estCorrecte,
            Points = points
        };

        bonusRound.Reponses.Add(answer);
        AppliquerPoints(session, playerId, points);

        return answer;
    }

    public void TerminerParTimeout(GameSession session, BonusRound bonusRound, SeriesConfig config)
    {
        var repondants = bonusRound.Reponses.Select(r => r.PlayerId).ToHashSet();

        foreach (var mise in bonusRound.Mises.Where(m => !repondants.Contains(m.PlayerId)))
        {
            var perte = -bonusScoring.ValeurPalier(config, mise.PalierIndex);

            bonusRound.Reponses.Add(new BonusAnswer
            {
                PlayerId = mise.PlayerId,
                Timestamp = DateTimeOffset.UtcNow,
                Reponse = "",
                EstCorrecte = false,
                Points = perte
            });

            AppliquerPoints(session, mise.PlayerId, perte);
        }
    }

    private static void AppliquerPoints(GameSession session, string playerId, int points)
    {
        var player = session.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (player is not null) player.Score += points;
    }
}
