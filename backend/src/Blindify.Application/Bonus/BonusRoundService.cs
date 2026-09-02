using Blindify.Application.Answers;
using Blindify.Application.Qcm;
using Blindify.Application.Rounds;
using Blindify.Application.Scoring;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;
using Blindify.Domain.Enums;

namespace Blindify.Application.Bonus;

public class BonusRoundService(IBonusScoringService bonusScoring, IAnswerMatcher answerMatcher, IQcmGenerator qcmGenerator) : IBonusRoundService
{
    // Cible forcée à Film pour les morceaux "disney" (même raison que RoundService.DemarrerRound) :
    // ni le titre réel de la chanson ni l'artiste crédité ne sont devinables pour ce type de contenu.
    // Sinon, même tirage 50/50 Titre/Auteur que RoundService.DemarrerRound (retour utilisateur :
    // la question bonus tombait toujours sur le titre, jamais l'artiste).
    //
    // Mode tiré au hasard parmi les 3 (retour utilisateur 2026-08-27 : la question bonus se
    // limitait à la réponse tapée, jamais QCM/Première lettre comme les rounds classiques) — même
    // tirage uniforme, indépendant de Cible. Qcm : options générées ici via le même pool que
    // RoundService.DemarrerRound (RoundService.PoolPourQcm), les feintes (GameHub) sont appliquées
    // plus tard côté BonusTimerCoordinator au moment de la diffusion, pas ici.
    public BonusRound CreerBonusRound(Track track, IReadOnlyList<Track> catalogueComplet, IReadOnlyList<string> tags, GameConfig config)
    {
        var cible = track.Tags.Contains("disney", StringComparer.OrdinalIgnoreCase)
            ? RoundCible.Film
            : Random.Shared.Next(2) == 0 && TitreVariantes.EstEligibleCommeCible(track.Title) ? RoundCible.Titre : RoundCible.Auteur;
        var mode = (RoundMode)Random.Shared.Next(3);
        // "Course" (retour utilisateur) : réservée au Qcm — répondre à voix haute ou par écrit n'a
        // pas de sens pour trancher qui a "buzzé" en premier, alors que les options Qcm restent le
        // même clic qu'un round normal, seul l'ORDRE d'arrivée compte côté serveur.
        var estCourse = mode == RoundMode.Qcm && Random.Shared.NextDouble() < config.ProbabiliteBonusCourse;

        var bonusRound = new BonusRound { TrackId = track.Id, Cible = cible, Mode = mode, EstCourse = estCourse };

        if (mode == RoundMode.Qcm)
        {
            var pool = RoundService.PoolPourQcm(cible, track, catalogueComplet, tags);
            var options = qcmGenerator.GenererOptions(track, pool, config, Random.Shared);
            bonusRound.QcmOptionTrackIds = options.OptionsTrackIds.ToList();
        }

        return bonusRound;
    }

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

    public BonusAnswer? SoumettreReponse(GameSession session, BonusRound bonusRound, SeriesConfig config, Track track, string playerId, string reponse, DateTimeOffset maintenant, Func<string, Track?>? resolveTrack = null)
    {
        if (session.EnPause) return null;
        if (bonusRound.DebutPhaseQuestion is null) return null;
        if (bonusRound.Reponses.Any(r => r.PlayerId == playerId)) return null;
        // Course déjà tranchée par un autre joueur (retour utilisateur) : "c'est seulement le
        // premier qui répond qui a ou perd les points" — les suivants n'affectent plus rien.
        if (bonusRound.EstCourse && bonusRound.Reponses.Count > 0) return null;

        var mise = bonusRound.Mises.FirstOrDefault(m => m.PlayerId == playerId);
        if (mise is null) return null;

        var reponsesAcceptables = bonusRound.Cible switch
        {
            RoundCible.Film => [FilmNameResolver.Resoudre(track)],
            RoundCible.Auteur => AuteurVariantes.Acceptables(track.Artist),
            _ => TitreVariantes.Acceptables(track.Title)
        };
        var estCorrecte = bonusRound.Mode switch
        {
            RoundMode.Qcm => RoundService.EstQcmCorrect(bonusRound.Cible, track, reponse, resolveTrack),
            RoundMode.PremiereLettre => reponsesAcceptables.Any(texte => EstPremiereLettreCorrecte(reponse, texte)),
            _ => reponsesAcceptables.Any(texte => answerMatcher.EstCorrecte(
                reponse, texte,
                session.Config.SeuilToleranceLevenshteinRatio,
                session.Config.LongueurMinimalePourTolerance,
                session.Config.LongueurMinimalePourToleranceFixe,
                session.Config.ToleranceFixeReponseCourte)),
        };
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

        // Course déjà tranchée (un joueur a répondu, juste ou faux) : les autres mises ne sont ni
        // gagnées ni perdues — voir SoumettreReponse. Seule l'absence TOTALE de réponse déclenche
        // la perte générale ci-dessous (comportement inchangé pour les autres modes).
        if (bonusRound.EstCourse && repondants.Count > 0) return;

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

    // Même logique que RoundService.EstPremiereLettreCorrecte, dupliquée plutôt que partagée par
    // instance (les deux services sont injectés/testés séparément) — voir architecture.md section 11.
    private bool EstPremiereLettreCorrecte(string reponse, string texteAttendu)
    {
        var normaliseeReponse = answerMatcher.Normaliser(reponse);
        var normaliseAttendu = answerMatcher.Normaliser(texteAttendu);
        return normaliseeReponse.Length > 0 && normaliseAttendu.Length > 0
               && normaliseeReponse[0] == normaliseAttendu[0];
    }
}
