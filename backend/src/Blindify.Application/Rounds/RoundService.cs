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

    /// <summary>Tirage pondéré favorisant les morceaux les moins joués. Poids = 1/(playCount+1)²,
    /// jamais nul : un morceau souvent joué reste tirable, juste beaucoup moins probable —
    /// pondération quadratique plutôt qu'une exclusion stricte (retour utilisateur du 2026-08-25,
    /// renforcée le 2026-09-13 : le poids linéaire d'origine ne dissuadait presque pas la
    /// réapparition d'un morceau qui vient d'être joué une seule fois, ex. juste après "Rejouer la
    /// partie" — écart de poids 1 vs 0.5 seulement, contre 1 vs 0.25 ici).</summary>
    private static Track TirerPondere(IReadOnlyList<Track> disponibles, Func<string, int> playCount)
    {
        var poids = disponibles.Select(t => 1.0 / Math.Pow(playCount(t.Id) + 1, 2)).ToList();
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
        round.Cible = ChoisirCible(answerMatcher, round.Mode, track);

        if (round.Mode == RoundMode.Qcm)
        {
            var pool = PoolPourQcm(round.Cible, track, catalogueComplet, tags);
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
    /// filtré est trop restreint pour fournir les 3 distracteurs + la bonne réponse — restreint ne
    /// veut pas seulement dire "moins de 4 morceaux" : un thème niche peut avoir 4+ morceaux mais
    /// moins de 3 AUTRES artistes distincts (retour utilisateur : "Angèle" deux fois dans un QCM
    /// "années 2020", catalogue avec exactement 2 titres d'Angèle tagués ainsi) — un pool insuffisant
    /// en diversité d'artistes forçait alors le filet de sécurité de QcmGenerator à dupliquer un
    /// libellé faute d'alternative, plutôt que d'élargir la recherche de distracteurs.</summary>
    /// <summary>Interne plutôt que privé : réutilisé par BonusRoundService.CreerBonusRound pour le
    /// même calcul de pool de distracteurs QCM (retour utilisateur : QCM aussi disponible en
    /// question bonus, pas seulement en round classique).</summary>
    internal static IReadOnlyList<Track> PoolPourQcm(RoundCible cible, Track correct, IReadOnlyList<Track> catalogueComplet, IReadOnlyList<string> tags)
    {
        if (cible == RoundCible.Film)
            return catalogueComplet.Where(t => t.Tags.Contains("disney", StringComparer.OrdinalIgnoreCase)).ToList();

        var filtre = FiltrerParTagsOuGenres(catalogueComplet, tags).ToList();
        var artistesAutresDistincts = filtre
            .Select(t => PremierAuteur(t.Artist))
            .Where(a => !string.Equals(a, PremierAuteur(correct.Artist), StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (filtre.Count >= 4 && artistesAutresDistincts >= 3) return filtre;

        // Repli catalogue complet (thème trop niche pour fournir 4 options avec assez d'artistes
        // distincts) : les morceaux "disney" restent exclus même ici (retour utilisateur : ils
        // réapparaissaient comme distracteurs QCM dans une série hors thème "disney", faute d'être
        // filtrés dans ce repli).
        return catalogueComplet.Where(t => !t.Tags.Contains("disney", StringComparer.OrdinalIgnoreCase)).ToList();
    }

    public RoundAnswer? SoumettreReponse(GameSession session, Round round, SeriesConfig seriesConfig, Track track, string playerId, string reponse, DateTimeOffset maintenant, Func<string, Track?>? resolveTrack = null)
    {
        if (session.EnPause) return null;
        if (round.DebutRound is null) return null;
        if (round.Reponses.Any(r => r.PlayerId == playerId)) return null;

        var reponsesAcceptables = ReponsesAcceptables(round.Cible, track);
        var estCorrecte = round.Mode switch
        {
            RoundMode.Qcm => EstQcmCorrect(round.Cible, track, reponse, resolveTrack),
            RoundMode.PremiereLettre => reponsesAcceptables.Any(texte => EstPremiereLettreCorrecte(reponse, texte)),
            _ => reponsesAcceptables.Any(texte => answerMatcher.EstCorrecte(
                reponse, texte,
                session.Config.SeuilToleranceLevenshteinRatio,
                session.Config.LongueurMinimalePourTolerance,
                session.Config.LongueurMinimalePourToleranceFixe,
                session.Config.ToleranceFixeReponseCourte)),
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
    /// possibles seulement pour Auteur (featurings, voir AuteurVariantes). Interne plutôt que privé :
    /// réutilisé par EstEligiblePremiereLettre ci-dessous.</summary>
    internal static IEnumerable<string> ReponsesAcceptables(RoundCible cible, Track track) => cible switch
    {
        RoundCible.Auteur => AuteurVariantes.Acceptables(track.Artist),
        RoundCible.Film => [FilmNameResolver.Resoudre(track)],
        _ => TitreVariantes.Acceptables(track.Title)
    };

    /// <summary>Cible du round/question bonus — partagée avec BonusRoundService.CreerBonusRound.
    /// Film forcé pour les morceaux "disney" (ni le titre réel ni l'artiste crédité n'y sont
    /// devinables, voir DemarrerRound ci-dessus), sinon 50/50 Titre/Auteur pondéré par
    /// l'éligibilité de chacun — longueur du titre (TitreVariantes.EstEligibleCommeCible) et,
    /// en Mode PremiereLettre, premier caractère effectivement une lettre (retour utilisateur :
    /// un auteur comme "50 Cent" ne matche aucune tuile A-Z côté joueur, voir
    /// EstEligiblePremiereLettre). Jamais totalement bloquant : si aucune des deux cibles n'est
    /// éligible en PremiereLettre (rare — titre trop long ET auteur commençant par un chiffre en
    /// même temps), retombe sur Auteur quand même plutôt que d'empêcher le round, même philosophie
    /// que le filet de sécurité de QcmGenerator.</summary>
    internal static RoundCible ChoisirCible(IAnswerMatcher answerMatcher, RoundMode mode, Track track)
    {
        if (track.Tags.Contains("disney", StringComparer.OrdinalIgnoreCase)) return RoundCible.Film;

        var titreEligible = TitreVariantes.EstEligibleCommeCible(track.Title)
            && (mode != RoundMode.PremiereLettre || EstEligiblePremiereLettre(answerMatcher, RoundCible.Titre, track));
        var auteurEligible = mode != RoundMode.PremiereLettre || EstEligiblePremiereLettre(answerMatcher, RoundCible.Auteur, track);

        if (titreEligible && auteurEligible) return Random.Shared.Next(2) == 0 ? RoundCible.Titre : RoundCible.Auteur;
        return titreEligible ? RoundCible.Titre : RoundCible.Auteur;
    }

    /// <summary>Un texte candidat n'est éligible comme cible "Première lettre" que si son premier
    /// caractère, une fois normalisé (AnswerMatcher.Normaliser : minuscule, sans accents), est une
    /// lettre — jamais un chiffre (ex. "50 Cent"), qui ne matcherait aucune des tuiles A-Z proposées
    /// côté joueur (voir _LetterAnswer, app/lib/screens/answer_phase_screen.dart). Les symboles de
    /// tête sont déjà neutralisés par la normalisation elle-même (ex. "$uicideboy$" -> "uicideboy",
    /// le premier caractère normalisé reste une lettre) — seuls les chiffres de tête posent
    /// réellement problème.</summary>
    internal static bool EstEligiblePremiereLettre(IAnswerMatcher answerMatcher, RoundCible cible, Track track) =>
        ReponsesAcceptables(cible, track).Any(texte =>
        {
            var normalise = answerMatcher.Normaliser(texte);
            return normalise.Length > 0 && char.IsLetter(normalise[0]);
        });

    /// <summary>Une réponse Qcm est correcte si le TrackId cliqué correspond, OU — repli — si le
    /// libellé RÉELLEMENT affiché pour ce TrackId est identique à celui de la bonne réponse. Interne
    /// plutôt que privé : réutilisé par BonusRoundService.SoumettreReponse (voir PoolPourQcm pour le
    /// même principe de partage).</summary>
    internal static bool EstQcmCorrect(RoundCible cible, Track correct, string reponseTrackId, Func<string, Track?>? resolveTrack)
    {
        if (reponseTrackId == correct.Id) return true;
        var soumis = resolveTrack?.Invoke(reponseTrackId);
        return soumis is not null && EstQcmEquivalent(cible, correct, soumis);
    }

    /// <summary>Deux morceaux différents peuvent afficher EXACTEMENT le même texte en Qcm — le filet
    /// de sécurité de QcmGenerator (catalogue trop restreint pour un thème) peut laisser passer un
    /// distracteur du même auteur/titre que la bonne réponse (retour utilisateur : "Myles Smith" en
    /// double, TrackId différent mais texte affiché identique, réponse comptée fausse à tort). Compare
    /// donc le même libellé que celui réellement affiché au joueur (Qcm : premier auteur seulement,
    /// voir app/lib/screens/answer_phase_screen.dart:_QcmAnswers.label / host/display.js:
    /// libelleOptionQcm) — jamais via AuteurVariantes/TitreVariantes (tolérance de saisie texte, pas
    /// équivalence d'affichage), et jamais via les DTOs QcmOptionDto (les feintes de GameHub n'y
    /// changent que le texte envoyé aux clients, jamais ces champs bruts ni le TrackId soumis).</summary>
    internal static bool EstQcmEquivalent(RoundCible cible, Track a, Track b) => cible switch
    {
        RoundCible.Auteur => string.Equals(PremierAuteur(a.Artist), PremierAuteur(b.Artist), StringComparison.OrdinalIgnoreCase),
        RoundCible.Film => string.Equals(FilmNameResolver.Resoudre(a), FilmNameResolver.Resoudre(b), StringComparison.OrdinalIgnoreCase),
        _ => string.Equals(a.Title, b.Title, StringComparison.OrdinalIgnoreCase),
    };

    private static string PremierAuteur(string artist) => artist.Split(',')[0].Trim();
}
