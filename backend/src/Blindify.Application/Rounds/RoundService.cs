using Blindify.Application.Answers;
using Blindify.Application.Qcm;
using Blindify.Application.Scoring;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;
using Blindify.Domain.Enums;

namespace Blindify.Application.Rounds;

public class RoundService(IScoringService scoring, IQcmGenerator qcmGenerator, IAnswerMatcher answerMatcher) : IRoundService
{
    public List<Track> SelectionnerMorceaux(IReadOnlyList<Track> pool, IReadOnlyList<string> tags, int nombre, HashSet<string> dejaUtilises, Func<string, int>? playCount = null)
    {
        // Pas de repli sur le catalogue complet quand un thème est explicitement demandé (retour
        // utilisateur : "je veux vraiment n'avoir QUE ce thème" — un round recevait un morceau
        // sans aucun rapport faute de pool filtré suffisant, contredisant cette promesse). Si le
        // pool filtré est insuffisant, on retourne moins que `nombre` : l'appelant
        // (GameHub.CreateGame/StartBonusRound) détecte déjà ce cas et refuse plutôt que de jouer
        // hors-thème en silence.
        var candidats = FiltrerParTagsOuGenres(pool, tags)
            .Where(t => !dejaUtilises.Contains(t.Id))
            .ToList();

        var resultat = new List<Track>();
        var disponibles = new List<Track>(candidats);

        while (resultat.Count < nombre && disponibles.Count > 0)
        {
            var track = playCount is null
                ? disponibles[Random.Shared.Next(disponibles.Count)]
                : TirerPondere(disponibles, playCount);
            resultat.Add(track);
            dejaUtilises.Add(track.Id);
            disponibles.Remove(track);
        }

        return resultat;
    }

    /// <summary>Tirage pondéré favorisant les morceaux les moins joués. Poids = 1/(playCount+1),
    /// jamais nul : un morceau souvent joué reste tirable, juste moins probable — pondération
    /// "douce" plutôt qu'une exclusion stricte (retour utilisateur du 2026-08-25).</summary>
    private static Track TirerPondere(IReadOnlyList<Track> disponibles, Func<string, int> playCount)
    {
        var poids = disponibles.Select(t => 1.0 / (playCount(t.Id) + 1)).ToList();
        var cible = Random.Shared.NextDouble() * poids.Sum();

        var cumul = 0.0;
        for (var i = 0; i < disponibles.Count; i++)
        {
            cumul += poids[i];
            if (cible < cumul) return disponibles[i];
        }

        return disponibles[^1]; // filet anti-arrondi flottant
    }

    public void DemarrerRound(Round round, Track track, IReadOnlyList<Track> catalogueComplet, IReadOnlyList<string> tags, GameConfig config, DateTimeOffset maintenant)
    {
        round.DebutRound = maintenant;
        // Cible forcée à Film pour les morceaux "disney" (retour utilisateur) : ni le titre réel de
        // la chanson ni l'artiste crédité (souvent la voix/l'acteur, ex. "Jason Weaver, Rowan
        // Atkinson, Laura Williams") ne sont devinables pour un joueur — le film dont est tiré le
        // morceau (Track.Album nettoyé, voir FilmNameResolver) est la question naturelle ici.
        round.Cible = track.Tags.Contains("disney", StringComparer.OrdinalIgnoreCase)
            ? RoundCible.Film
            : Random.Shared.Next(2) == 0 && TitreVariantes.EstEligibleCommeCible(track.Title) ? RoundCible.Titre : RoundCible.Auteur;

        if (round.Mode == RoundMode.Qcm)
        {
            var pool = PoolPourQcm(round.Cible, catalogueComplet, tags);
            var options = qcmGenerator.GenererOptions(track, pool, config, Random.Shared);
            round.QcmOptionTrackIds = options.OptionsTrackIds.ToList();
        }
    }

    /// <summary>Pool de tirage des distracteurs QCM — retour utilisateur : une question Film (Disney)
    /// proposait "Cœur de pirate" en option, sans aucun rapport, parce que les distracteurs étaient
    /// tirés du catalogue complet plutôt que du thème réellement sélectionné pour la partie/série.
    /// Cible Film : restreint aux autres morceaux "disney" (seuls à avoir un nom de film cohérent) —
    /// jamais de repli catalogue complet ici, mieux vaut moins d'options que des incohérentes.
    /// Sinon : respecte le thème (tags), avec repli sur le catalogue complet seulement si le pool
    /// filtré est trop restreint pour fournir les 3 distracteurs + la bonne réponse.</summary>
    private static IReadOnlyList<Track> PoolPourQcm(RoundCible cible, IReadOnlyList<Track> catalogueComplet, IReadOnlyList<string> tags)
    {
        if (cible == RoundCible.Film)
            return catalogueComplet.Where(t => t.Tags.Contains("disney", StringComparer.OrdinalIgnoreCase)).ToList();

        var filtre = FiltrerParTagsOuGenres(catalogueComplet, tags).ToList();
        return filtre.Count >= 4 ? filtre : catalogueComplet;
    }

    public RoundAnswer? SoumettreReponse(GameSession session, Round round, SeriesConfig seriesConfig, Track track, string playerId, string reponse, DateTimeOffset maintenant)
    {
        if (session.EnPause) return null;
        if (round.DebutRound is null) return null;
        if (round.Reponses.Any(r => r.PlayerId == playerId)) return null;

        var reponsesAcceptables = ReponsesAcceptables(round.Cible, track);
        var estCorrecte = round.Mode switch
        {
            RoundMode.Qcm => reponse == track.Id,
            RoundMode.PremiereLettre => reponsesAcceptables.Any(texte => EstPremiereLettreCorrecte(reponse, texte)),
            _ => reponsesAcceptables.Any(texte => answerMatcher.EstCorrecte(reponse, texte, session.Config.SeuilToleranceLevenshteinRatio)),
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

    // Retour utilisateur : une série thématique "années 2010" recevait des chansons Disney, parce
    // que ~45 % du catalogue "disney" est aussi tagué par décennie (les remakes/films récents) —
    // le filtre matchait bien la décennie, mais le résultat ne "sonnait" pas comme le thème
    // attendu. Disney a un univers musical trop particulier (comédies musicales, voix de
    // doublage) pour se mélanger à un thème générique : exclu de tout thème AUTRE que "disney"
    // lui-même, quand bien même un morceau correspondrait par ailleurs (décennie, genre).
    private static IEnumerable<Track> FiltrerParTagsOuGenres(IReadOnlyList<Track> pool, IReadOnlyList<string> tags)
    {
        if (tags.Count == 0) return pool;

        var candidats = pool.Where(t => t.Tags.Intersect(tags).Any() || t.Genres.Intersect(tags).Any());

        if (!tags.Contains("disney", StringComparer.OrdinalIgnoreCase))
            candidats = candidats.Where(t => !t.Tags.Contains("disney", StringComparer.OrdinalIgnoreCase));

        return candidats;
    }

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

    /// <summary>Textes acceptés comme bonne réponse pour la cible du round — plusieurs valeurs
    /// possibles seulement pour Auteur (featurings, voir AuteurVariantes).</summary>
    private static IEnumerable<string> ReponsesAcceptables(RoundCible cible, Track track) => cible switch
    {
        RoundCible.Auteur => AuteurVariantes.Acceptables(track.Artist),
        RoundCible.Film => [FilmNameResolver.Resoudre(track)],
        _ => TitreVariantes.Acceptables(track.Title)
    };
}
