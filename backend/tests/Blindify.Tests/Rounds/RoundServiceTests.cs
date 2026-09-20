using Blindify.Application.Answers;
using Blindify.Application.Qcm;
using Blindify.Application.Rounds;
using Blindify.Application.Scoring;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;
using Blindify.Domain.Enums;

namespace Blindify.Tests.Rounds;

public class RoundServiceTests
{
    private readonly RoundService _service = new(new ScoringService(), new QcmGenerator(), new AnswerMatcher(), new AnneeQcmGenerator());

    private static Track NouveauTrack(string id, List<string>? genres = null, List<string>? tags = null) => new()
    {
        Id = id,
        Title = $"Titre {id}",
        Artist = "Artiste",
        FilePath = $"audio/{id}.mp3",
        Genres = genres ?? [],
        Tags = tags ?? []
    };

    private static GameSession NouvelleSession(params Player[] joueurs) => new()
    {
        Id = "ABCDE",
        Config = new GameConfig(),
        Players = joueurs.ToList(),
        HostSecret = "test-secret"
    };

    private static SeriesConfig NouveauConfig() => new()
    {
        DureeFenetreReponseMs = 10_000,
        PointsMax = 100,
        PointsMin = 20,
        PenaliteMauvaiseReponseRatio = 0.5,
        PenaliteAbsenceReponse = -5
    };

    [Fact]
    public void SelectionnerMorceaux_TireLeNombreDemande_SansRepetition()
    {
        var pool = Enumerable.Range(0, 10).Select(i => NouveauTrack($"t{i}", tags: ["pop"])).ToList();
        var dejaUtilises = new HashSet<string>();

        var resultat = _service.SelectionnerMorceaux(pool, tags: ["pop"], nombre: 5, dejaUtilises);

        Assert.Equal(5, resultat.Count);
        Assert.Equal(5, resultat.Select(t => t.Id).Distinct().Count());
        Assert.Equal(5, dejaUtilises.Count);
    }

    [Fact]
    public void SelectionnerMorceaux_RespecteDejaUtilises_EntreDeuxAppels()
    {
        var pool = Enumerable.Range(0, 6).Select(i => NouveauTrack($"t{i}")).ToList();
        var dejaUtilises = new HashSet<string>();

        var premier = _service.SelectionnerMorceaux(pool, tags: [], nombre: 3, dejaUtilises);
        var second = _service.SelectionnerMorceaux(pool, tags: [], nombre: 3, dejaUtilises);

        Assert.Empty(premier.Select(t => t.Id).Intersect(second.Select(t => t.Id)));
    }

    [Fact]
    public void SelectionnerMorceaux_PoolTagInsuffisant_NeComplevePasAvecDesMorceauxHorsTheme()
    {
        // Retour utilisateur : "je veux vraiment n'avoir QUE ce thème" — une série "années 2020"
        // recevait des morceaux sans aucun rapport faute de pool suffisant. Le repli sur le
        // catalogue complet a été retiré : mieux vaut retourner moins que demandé (l'appelant,
        // GameHub.CreateGame, refuse alors la partie avec un message clair) que de jouer hors-thème.
        var pool = new List<Track> { NouveauTrack("niche", tags: ["niche"]), NouveauTrack("a"), NouveauTrack("b"), NouveauTrack("c") };
        var dejaUtilises = new HashSet<string>();

        var resultat = _service.SelectionnerMorceaux(pool, tags: ["niche"], nombre: 3, dejaUtilises);

        Assert.Single(resultat);
        Assert.Equal("niche", resultat[0].Id);
    }

    [Fact]
    public void SelectionnerMorceaux_ThemeAutreQueDisney_ExclutLesMorceauxDisneyMemeSiTagCorrespond()
    {
        // Retour utilisateur : une série "années 2010" recevait des chansons Disney, parce que
        // ~45 % du catalogue "disney" est aussi tagué par décennie (remakes/films récents) — le
        // tag correspondait bien, mais Disney a un univers musical trop particulier pour se
        // mélanger à un thème générique.
        var disneyMaisAnnees2010 = NouveauTrack("disney-2010", tags: ["disney", "annees-2010"]);
        var pool = new List<Track>
        {
            disneyMaisAnnees2010,
            NouveauTrack("a", tags: ["annees-2010"]),
            NouveauTrack("b", tags: ["annees-2010"]),
            NouveauTrack("c", tags: ["annees-2010"]),
        };
        var dejaUtilises = new HashSet<string>();

        var resultat = _service.SelectionnerMorceaux(pool, tags: ["annees-2010"], nombre: 4, dejaUtilises);

        Assert.DoesNotContain(resultat, t => t.Id == "disney-2010");
        Assert.Equal(3, resultat.Count);
    }

    [Fact]
    public void SelectionnerMorceaux_AvecPlayCount_FavoriseLesMoinsJouesSansExclureLesAutres()
    {
        // Retour utilisateur (2026-08-25) : playCount (data/stats.json) existait déjà comme
        // compteur mais n'influençait jamais le tirage — mêmes morceaux qui revenaient d'une
        // partie à l'autre sur un même thème. Pondération douce : jamais d'exclusion stricte.
        var jamaisJoue = NouveauTrack("jamais");
        var souventJoue = NouveauTrack("souvent");
        var pool = new List<Track> { jamaisJoue, souventJoue };
        // Écart modéré (0 vs 5) plutôt qu'extrême : avec la pondération quadratique, un écart de 20
        // rendrait le morceau "souvent" quasi jamais tiré sur 500 essais, au risque de faire échouer
        // par pur hasard l'assertion "jamais totalement exclu" ci-dessous (test flaky).
        var playCounts = new Dictionary<string, int> { ["jamais"] = 0, ["souvent"] = 5 };

        const int nombreTirages = 500;
        var tiragesJamaisJoue = 0;
        for (var i = 0; i < nombreTirages; i++)
        {
            var dejaUtilises = new HashSet<string>();
            var resultat = _service.SelectionnerMorceaux(pool, tags: [], nombre: 1, dejaUtilises, id => playCounts[id]);
            if (resultat[0].Id == "jamais") tiragesJamaisJoue++;
        }

        Assert.True(tiragesJamaisJoue > nombreTirages * 0.9, $"attendu > 90% de tirages sur le morceau jamais joué, obtenu {tiragesJamaisJoue}/{nombreTirages}");
        Assert.True(tiragesJamaisJoue < nombreTirages, "le morceau souvent joué ne doit jamais être totalement exclu");
    }

    [Fact]
    public void SelectionnerMorceaux_ThemeDisney_IncludeLesMorceauxDisney()
    {
        var disneyMaisAnnees2010 = NouveauTrack("disney-2010", tags: ["disney", "annees-2010"]);
        var pool = new List<Track> { disneyMaisAnnees2010, NouveauTrack("a", tags: ["annees-2010"]) };
        var dejaUtilises = new HashSet<string>();

        var resultat = _service.SelectionnerMorceaux(pool, tags: ["disney"], nombre: 1, dejaUtilises);

        Assert.Single(resultat);
        Assert.Equal("disney-2010", resultat[0].Id);
    }

    [Fact]
    public void DemarrerRound_ModeQcm_GenereQuatreOptions()
    {
        var correct = NouveauTrack("a", genres: ["pop"]);
        var catalogue = new List<Track> { correct, NouveauTrack("b", genres: ["pop"]), NouveauTrack("c", genres: ["pop"]), NouveauTrack("d", genres: ["pop"]) };
        var config = new GameConfig { ProbabiliteQcmPiege = 0 };
        var round = new Round { TrackId = correct.Id, Mode = RoundMode.Qcm };

        _service.DemarrerRound(round, correct, catalogue, tags: [], config, DateTimeOffset.UtcNow);

        Assert.NotNull(round.QcmOptionTrackIds);
        Assert.Equal(4, round.QcmOptionTrackIds!.Count);
        Assert.Contains(correct.Id, round.QcmOptionTrackIds);
        Assert.NotNull(round.DebutRound);
    }

    [Fact]
    public void DemarrerRound_ModeQcm_PoolThematiqueTropPauvreEnArtistes_ElargitAuCatalogueComplet()
    {
        // Retour utilisateur : "Angèle" apparaissait deux fois dans un QCM "années 2020" — le thème
        // avait bien 4 morceaux, mais seulement 2 AUTRES artistes distincts ("b" partage l'artiste
        // de la bonne réponse), insuffisant pour les 3 distracteurs de QcmGenerator. Avant le
        // correctif, PoolPourQcm ne regardait que le nombre de morceaux (4 >= 4, jugé "suffisant")
        // et ne s'élargissait jamais au catalogue complet, forçant le filet de sécurité de
        // QcmGenerator à réutiliser "b" (même libellé affiché que la bonne réponse) plutôt que "e"
        // (hors thème mais seul artiste réellement disponible).
        var correct = new Track { Id = "a", Title = "T-a", Artist = "Angele", FilePath = "audio/a.mp3", Tags = ["annees-2020"] };
        var memeArtiste = new Track { Id = "b", Title = "T-b", Artist = "Angele", FilePath = "audio/b.mp3", Tags = ["annees-2020"] };
        var autre1 = new Track { Id = "c", Title = "T-c", Artist = "Bob", FilePath = "audio/c.mp3", Tags = ["annees-2020"] };
        var autre2 = new Track { Id = "d", Title = "T-d", Artist = "Carl", FilePath = "audio/d.mp3", Tags = ["annees-2020"] };
        var horsTheme = new Track { Id = "e", Title = "T-e", Artist = "Dave", FilePath = "audio/e.mp3" };
        var catalogue = new List<Track> { correct, memeArtiste, autre1, autre2, horsTheme };
        var config = new GameConfig { ProbabiliteQcmPiege = 0 };
        var round = new Round { TrackId = correct.Id, Mode = RoundMode.Qcm };

        _service.DemarrerRound(round, correct, catalogue, tags: ["annees-2020"], config, DateTimeOffset.UtcNow);

        Assert.Equal(4, round.QcmOptionTrackIds!.Count);
        Assert.DoesNotContain("b", round.QcmOptionTrackIds);
        Assert.Contains("e", round.QcmOptionTrackIds);
    }

    [Fact]
    public void DemarrerRound_ModeTapeReponse_NeGenereAucuneOption()
    {
        var correct = NouveauTrack("a");
        var round = new Round { TrackId = correct.Id, Mode = RoundMode.TapeReponse };

        _service.DemarrerRound(round, correct, [correct], tags: [], new GameConfig(), DateTimeOffset.UtcNow);

        Assert.Null(round.QcmOptionTrackIds);
    }

    [Fact]
    public void SoumettreReponse_BonneReponseQcm_CrediteLeJoueur()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = NouveauTrack("a");
        var round = new Round { TrackId = "a", Mode = RoundMode.Qcm, DebutRound = DateTimeOffset.UtcNow, QcmOptionTrackIds = ["a", "b", "c", "d"] };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "a", round.DebutRound!.Value);

        Assert.NotNull(reponse);
        Assert.True(reponse!.EstCorrecte);
        Assert.Equal(100, reponse.Points);
        Assert.Equal(100, joueur.Score);
    }

    [Fact]
    public void SoumettreReponse_MauvaiseReponse_PenaliseAvecLeRatioConfigure()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = NouveauTrack("a");
        var round = new Round { TrackId = "a", Mode = RoundMode.Qcm, DebutRound = DateTimeOffset.UtcNow, QcmOptionTrackIds = ["a", "b", "c", "d"] };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "b", round.DebutRound!.Value);

        Assert.False(reponse!.EstCorrecte);
        Assert.Equal(-50, reponse.Points);
        Assert.Equal(-50, joueur.Score);
    }

    [Fact]
    public void SoumettreReponse_CibleAuteur_QcmDeuxOptionsMemeAuteur_LesDeuxSontAcceptees()
    {
        // Retour utilisateur : le filet de sécurité de QcmGenerator peut, sur un catalogue trop
        // restreint pour un thème, laisser passer un distracteur du même auteur que la bonne réponse
        // (deux morceaux différents de "Myles Smith" dans le même QCM) — les deux options affichent
        // alors le même texte, un TrackId différent ne doit plus suffire à compter la réponse fausse.
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var correct = new Track { Id = "a", Title = "Stargazing", Artist = "Myles Smith", FilePath = "audio/a.mp3" };
        var doublon = new Track { Id = "b", Title = "Nice To Meet You", Artist = "Myles Smith", FilePath = "audio/b.mp3" };
        var catalogue = new[] { correct, doublon }.ToDictionary(t => t.Id);
        var round = new Round { TrackId = "a", Mode = RoundMode.Qcm, Cible = RoundCible.Auteur, DebutRound = DateTimeOffset.UtcNow, QcmOptionTrackIds = ["a", "b", "c", "d"] };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), correct, "p1", round.Id, "b", round.DebutRound!.Value, id => catalogue.GetValueOrDefault(id));

        Assert.True(reponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_CibleAuteur_QcmAuteurDifferent_ResteRefusee()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var correct = new Track { Id = "a", Title = "Stargazing", Artist = "Myles Smith", FilePath = "audio/a.mp3" };
        var autreAuteur = new Track { Id = "b", Title = "Autre chanson", Artist = "Teddy Swims", FilePath = "audio/b.mp3" };
        var catalogue = new[] { correct, autreAuteur }.ToDictionary(t => t.Id);
        var round = new Round { TrackId = "a", Mode = RoundMode.Qcm, Cible = RoundCible.Auteur, DebutRound = DateTimeOffset.UtcNow, QcmOptionTrackIds = ["a", "b", "c", "d"] };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), correct, "p1", round.Id, "b", round.DebutRound!.Value, id => catalogue.GetValueOrDefault(id));

        Assert.False(reponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_ModeQcm_SansResolveTrack_ResteEnComparaisonStricteParId()
    {
        // resolveTrack optionnel (null) : les appelants qui n'en ont pas besoin (tests existants,
        // futurs appels) gardent le comportement d'origine plutôt que de planter ou de forcer le
        // repli par libellé.
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var correct = new Track { Id = "a", Title = "Stargazing", Artist = "Myles Smith", FilePath = "audio/a.mp3" };
        var round = new Round { TrackId = "a", Mode = RoundMode.Qcm, Cible = RoundCible.Auteur, DebutRound = DateTimeOffset.UtcNow, QcmOptionTrackIds = ["a", "b", "c", "d"] };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), correct, "p1", round.Id, "b", round.DebutRound!.Value);

        Assert.False(reponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_PremiereLettreCorrecte_EstValidee()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = NouveauTrack("a"); // Title = "Titre a"
        var round = new Round { TrackId = "a", Mode = RoundMode.PremiereLettre, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "t", round.DebutRound!.Value);

        Assert.True(reponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_PremiereLettreCasseDifferente_EstValidee()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = NouveauTrack("a");
        var round = new Round { TrackId = "a", Mode = RoundMode.PremiereLettre, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "T", round.DebutRound!.Value);

        Assert.True(reponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_PremiereLettreAccentuee_EstNormaliseeAvantComparaison()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = new Track { Id = "a", Title = "Étoile", Artist = "Artiste", FilePath = "audio/a.mp3" };
        var round = new Round { TrackId = "a", Mode = RoundMode.PremiereLettre, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "e", round.DebutRound!.Value);

        Assert.True(reponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_PremiereLettreIncorrecte_EstRefusee()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = NouveauTrack("a");
        var round = new Round { TrackId = "a", Mode = RoundMode.PremiereLettre, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "x", round.DebutRound!.Value);

        Assert.False(reponse!.EstCorrecte);
    }

    [Fact]
    public void DemarrerRound_TireLaCible_LesDeuxValeursApparaissentSurPlusieursTirages()
    {
        var correct = NouveauTrack("a");
        var cibles = new HashSet<RoundCible>();

        for (var i = 0; i < 50; i++)
        {
            var round = new Round { TrackId = correct.Id, Mode = RoundMode.TapeReponse };
            _service.DemarrerRound(round, correct, [correct], tags: [], new GameConfig(), DateTimeOffset.UtcNow);
            cibles.Add(round.Cible);
        }

        Assert.Contains(RoundCible.Titre, cibles);
        Assert.Contains(RoundCible.Auteur, cibles);
    }

    [Fact]
    public void DemarrerRound_MorceauDisney_CibleToujoursFilm()
    {
        var correct = NouveauTrack("a", tags: ["disney"]);

        for (var i = 0; i < 20; i++)
        {
            var round = new Round { TrackId = correct.Id, Mode = RoundMode.TapeReponse };
            _service.DemarrerRound(round, correct, [correct], tags: [], new GameConfig(), DateTimeOffset.UtcNow);
            Assert.Equal(RoundCible.Film, round.Cible);
        }
    }

    // Retour utilisateur : un auteur comme "50 Cent" (premier caractère normalisé = chiffre) ne
    // matche aucune des tuiles A-Z proposées côté joueur en Mode PremiereLettre — round injouable
    // pour ce joueur si Auteur était quand même tiré comme cible.
    [Fact]
    public void DemarrerRound_ModePremiereLettre_AuteurCommenceParUnChiffre_CibleNestJamaisAuteur()
    {
        var correct = new Track { Id = "a", Title = "Titre Court", Artist = "50 Cent", FilePath = "audio/a.mp3", Genres = [], Tags = [] };

        for (var i = 0; i < 20; i++)
        {
            var round = new Round { TrackId = correct.Id, Mode = RoundMode.PremiereLettre };
            _service.DemarrerRound(round, correct, [correct], tags: [], new GameConfig(), DateTimeOffset.UtcNow);
            Assert.Equal(RoundCible.Titre, round.Cible);
        }
    }

    // La restriction ci-dessus est spécifique à PremiereLettre — un auteur commençant par un
    // chiffre reste une cible valide dans les autres modes (rien à corriger côté QCM/tape la
    // réponse, qui n'exigent pas de faire correspondre un unique caractère à une tuile A-Z).
    [Fact]
    public void DemarrerRound_ModeTapeReponse_AuteurCommenceParUnChiffre_CibleAuteurResteTiree()
    {
        var correct = new Track { Id = "a", Title = "Titre Court", Artist = "50 Cent", FilePath = "audio/a.mp3", Genres = [], Tags = [] };
        var cibles = new HashSet<RoundCible>();

        for (var i = 0; i < 50; i++)
        {
            var round = new Round { TrackId = correct.Id, Mode = RoundMode.TapeReponse };
            _service.DemarrerRound(round, correct, [correct], tags: [], new GameConfig(), DateTimeOffset.UtcNow);
            cibles.Add(round.Cible);
        }

        Assert.Contains(RoundCible.Auteur, cibles);
    }

    // Filet de sécurité : si NI le titre (trop long) NI l'auteur (commence par un chiffre) ne sont
    // éligibles en PremiereLettre, le round doit quand même démarrer (repli sur Auteur) plutôt que
    // de bloquer la partie faute d'alternative — même philosophie que le filet de QcmGenerator.
    [Fact]
    public void DemarrerRound_ModePremiereLettre_NiTitreNiAuteurEligibles_RetombeSurAuteurSansBloquer()
    {
        var correct = new Track
        {
            Id = "a", Title = new string('x', 50), Artist = "50 Cent", FilePath = "audio/a.mp3", Genres = [], Tags = []
        };

        var round = new Round { TrackId = correct.Id, Mode = RoundMode.PremiereLettre };
        _service.DemarrerRound(round, correct, [correct], tags: [], new GameConfig(), DateTimeOffset.UtcNow);

        Assert.Equal(RoundCible.Auteur, round.Cible);
    }

    [Fact]
    public void DemarrerRound_CibleFilm_OptionsQcmUniquementDesMorceauxDisney()
    {
        // Retour utilisateur : une question Film (Disney) proposait "Cœur de pirate" comme option,
        // sans aucun rapport avec un film Disney -- les distracteurs QCM venaient du catalogue
        // complet plutôt que d'être restreints aux autres morceaux "disney" (seuls à avoir un nom
        // de film cohérent à proposer). Le thème sélectionné pour la partie (tags: []) ici n'a pas
        // d'importance : la cible Film court-circuite toujours vers le pool "disney" uniquement.
        var correct = NouveauTrack("a", tags: ["disney"]);
        var catalogue = new List<Track>
        {
            correct,
            NouveauTrack("disney-b", tags: ["disney"]),
            NouveauTrack("disney-c", tags: ["disney"]),
            NouveauTrack("disney-d", tags: ["disney"]),
            NouveauTrack("hors-theme-1"),
            NouveauTrack("hors-theme-2"),
        };
        var config = new GameConfig { ProbabiliteQcmPiege = 0 };
        var round = new Round { TrackId = correct.Id, Mode = RoundMode.Qcm };

        _service.DemarrerRound(round, correct, catalogue, tags: [], config, DateTimeOffset.UtcNow);

        Assert.Equal(RoundCible.Film, round.Cible);
        Assert.DoesNotContain("hors-theme-1", round.QcmOptionTrackIds!);
        Assert.DoesNotContain("hors-theme-2", round.QcmOptionTrackIds!);
        var options = round.QcmOptionTrackIds!.Select(id => catalogue.First(t => t.Id == id));
        Assert.All(options, t => Assert.Contains("disney", t.Tags, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void DemarrerRound_CibleAutreQueFilm_ReplitCatalogueCompletExcluTDisney()
    {
        // Retour utilisateur : une série hors thème "disney" (ex. "années 2010") proposait quand
        // même des morceaux Disney comme options QCM, dès que le thème filtré ne fournissait pas
        // assez de distracteurs et retombait sur le catalogue complet SANS réappliquer l'exclusion
        // disney (voir RoundService.PoolPourQcm).
        var correct = NouveauTrack("a", tags: ["annees-2010"]);
        var catalogue = new List<Track>
        {
            correct,
            NouveauTrack("b", tags: ["annees-2010"]),
            NouveauTrack("disney-a", tags: ["disney", "annees-2010"]),
            NouveauTrack("disney-b", tags: ["disney"]),
            NouveauTrack("hors-theme-1"),
            NouveauTrack("hors-theme-2"),
        };
        var config = new GameConfig { ProbabiliteQcmPiege = 0 };
        var round = new Round { TrackId = correct.Id, Mode = RoundMode.Qcm };

        _service.DemarrerRound(round, correct, catalogue, tags: ["annees-2010"], config, DateTimeOffset.UtcNow);

        Assert.NotEqual(RoundCible.Film, round.Cible);
        var options = round.QcmOptionTrackIds!.Select(id => catalogue.First(t => t.Id == id));
        Assert.All(options, t => Assert.DoesNotContain("disney", t.Tags, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void SoumettreReponse_CibleFilm_CompareAuNomDuFilmNettoyeDeLAlbum()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = new Track
        {
            Id = "a",
            Title = "Un rêve est un souhait",
            Artist = "Ilene Woods",
            Album = "Cendrillon (Original Motion Picture Soundtrack)",
            FilePath = "audio/a.mp3",
            Tags = ["disney"]
        };
        var round = new Round { TrackId = "a", Mode = RoundMode.TapeReponse, Cible = RoundCible.Film, DebutRound = DateTimeOffset.UtcNow };

        var mauvaiseReponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "Un rêve est un souhait", round.DebutRound!.Value);
        Assert.False(mauvaiseReponse!.EstCorrecte);

        round.Reponses.Clear();
        var bonneReponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "Cendrillon", round.DebutRound!.Value);
        Assert.True(bonneReponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_CibleTitre_ReponseSansLeContenuEntreParentheses_EstAcceptee()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = new Track { Id = "a", Title = "Sweat (A La La La La Long)", Artist = "Inner Circle", FilePath = "audio/a.mp3" };
        var round = new Round { TrackId = "a", Mode = RoundMode.TapeReponse, Cible = RoundCible.Titre, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "Sweat", round.DebutRound!.Value);

        Assert.True(reponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_CibleTitre_ReponseCompleteAvecParentheses_ResteAcceptee()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = new Track { Id = "a", Title = "Sweat (A La La La La Long)", Artist = "Inner Circle", FilePath = "audio/a.mp3" };
        var round = new Round { TrackId = "a", Mode = RoundMode.TapeReponse, Cible = RoundCible.Titre, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "Sweat (A La La La La Long)", round.DebutRound!.Value);

        Assert.True(reponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_CibleAuteur_UnSeulDesPlusieursAuteursSuffit()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = new Track { Id = "a", Title = "Gone Gone Gone", Artist = "David Guetta, Tones And I, Teddy Swims", FilePath = "audio/a.mp3" };
        var round = new Round { TrackId = "a", Mode = RoundMode.TapeReponse, Cible = RoundCible.Auteur, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "Teddy Swims", round.DebutRound!.Value);

        Assert.True(reponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_CibleAuteur_LeTitreNestPasAccepte()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = new Track { Id = "a", Title = "Gone Gone Gone", Artist = "David Guetta, Tones And I, Teddy Swims", FilePath = "audio/a.mp3" };
        var round = new Round { TrackId = "a", Mode = RoundMode.TapeReponse, Cible = RoundCible.Auteur, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "Gone Gone Gone", round.DebutRound!.Value);

        Assert.False(reponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_CibleAuteurPremiereLettre_MatchNimporteLequelDesAuteurs()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = new Track { Id = "a", Title = "Gone Gone Gone", Artist = "David Guetta, Tones And I, Teddy Swims", FilePath = "audio/a.mp3" };
        var round = new Round { TrackId = "a", Mode = RoundMode.PremiereLettre, Cible = RoundCible.Auteur, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "t", round.DebutRound!.Value);

        Assert.True(reponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_DeuxiemeEssaiDuMemeJoueur_EstRejete()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = NouveauTrack("a");
        var round = new Round { TrackId = "a", Mode = RoundMode.Qcm, DebutRound = DateTimeOffset.UtcNow, QcmOptionTrackIds = ["a", "b", "c", "d"] };

        _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "a", round.DebutRound!.Value);
        var deuxieme = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "b", round.DebutRound!.Value);

        Assert.Null(deuxieme);
        Assert.Equal(100, joueur.Score);
    }

    [Fact]
    public void SoumettreReponse_PartieEnPause_EstRejetee()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        session.EnPause = true;
        var track = NouveauTrack("a");
        var round = new Round { TrackId = "a", Mode = RoundMode.Qcm, DebutRound = DateTimeOffset.UtcNow, QcmOptionTrackIds = ["a", "b", "c", "d"] };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "a", round.DebutRound!.Value);

        Assert.Null(reponse);
    }

    [Fact]
    public void TerminerParTimeout_PenaliseUniquementLesJoueursSansReponse()
    {
        var alice = new Player { PlayerId = "p1", Nom = "Alice" };
        var bob = new Player { PlayerId = "p2", Nom = "Bob" };
        var session = NouvelleSession(alice, bob);
        var track = NouveauTrack("a");
        var round = new Round { TrackId = "a", Mode = RoundMode.Qcm, DebutRound = DateTimeOffset.UtcNow, QcmOptionTrackIds = ["a", "b", "c", "d"] };
        var config = NouveauConfig();

        _service.SoumettreReponse(session, round, config, track, "p1", round.Id, "a", round.DebutRound!.Value);
        _service.TerminerParTimeout(session, round, config);

        Assert.Equal(100, alice.Score);
        Assert.Equal(-5, bob.Score);

        var reponseBob = round.Reponses.Single(r => r.PlayerId == "p2");
        Assert.False(reponseBob.EstCorrecte);
        Assert.Equal(-5, reponseBob.Points);
    }

    [Fact]
    public void ValiderManuellement_BasculeFauxVersJuste_CrediteLeDelta()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = NouveauTrack("a");
        var round = new Round { TrackId = "a", Mode = RoundMode.TapeReponse, DebutRound = DateTimeOffset.UtcNow };
        var config = NouveauConfig();

        // Réponse jugée fausse automatiquement (ex. faute de frappe hors tolérance) : -50.
        _service.SoumettreReponse(session, round, config, track, "p1", round.Id, "Reponse hors tolerance", round.DebutRound!.Value);
        Assert.Equal(-50, joueur.Score);

        var revalidee = _service.ValiderManuellement(session, round, config, "p1", estCorrecte: true);

        Assert.NotNull(revalidee);
        Assert.True(revalidee!.EstCorrecte);
        Assert.Equal(100, revalidee.Points);
        Assert.Equal(100, joueur.Score);
    }

    [Fact]
    public void ValiderManuellement_JoueurSansReponse_RetourneNull()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var round = new Round { TrackId = "a", Mode = RoundMode.TapeReponse, DebutRound = DateTimeOffset.UtcNow };

        var resultat = _service.ValiderManuellement(session, round, NouveauConfig(), "p1", estCorrecte: true);

        Assert.Null(resultat);
    }

    // ----- V2 (socle statistiques) : RoundOption / RoundAnswer enrichis -----

    [Fact]
    public void SoumettreReponse_ModeQcm_RenseigneOptionChoisieDepuisRoundOptions()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = NouveauTrack("a");
        var round = new Round
        {
            TrackId = "a",
            Mode = RoundMode.Qcm,
            DebutRound = DateTimeOffset.UtcNow,
            QcmOptionTrackIds = ["a", "b", "c", "d"],
            Options =
            [
                new RoundOption { TrackId = "a", TexteAffiche = "Titre a", EstFeinte = false, EstPiege = false },
                new RoundOption { TrackId = "b", TexteAffiche = "Titre b", EstFeinte = true, EstPiege = false },
                new RoundOption { TrackId = "c", TexteAffiche = "Titre c", EstFeinte = false, EstPiege = true },
                new RoundOption { TrackId = "d", TexteAffiche = "Titre d", EstFeinte = false, EstPiege = false },
            ],
        };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "c", round.DebutRound!.Value);

        Assert.Equal("c", reponse!.OptionChoisieTrackId);
        Assert.False(reponse.OptionChoisieEstFeinte);
        Assert.True(reponse.OptionChoisieEstPiege);
        Assert.True(reponse.TempsReponseMs >= 0);
    }

    [Fact]
    public void SoumettreReponse_ModeTapeReponse_OptionChoisieResteNull()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = NouveauTrack("a");
        var round = new Round { TrackId = "a", Mode = RoundMode.TapeReponse, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "Titre a", round.DebutRound!.Value);

        Assert.Null(reponse!.OptionChoisieTrackId);
        Assert.False(reponse.OptionChoisieEstFeinte);
        Assert.False(reponse.OptionChoisieEstPiege);
    }

    [Fact]
    public void TerminerParTimeout_MarqueLesEntreesSynthetiquesCommeAbsentes()
    {
        var alice = new Player { PlayerId = "p1", Nom = "Alice" };
        var bob = new Player { PlayerId = "p2", Nom = "Bob" };
        var session = NouvelleSession(alice, bob);
        var track = NouveauTrack("a");
        var round = new Round { TrackId = "a", Mode = RoundMode.Qcm, DebutRound = DateTimeOffset.UtcNow, QcmOptionTrackIds = ["a", "b", "c", "d"] };
        var config = NouveauConfig();

        _service.SoumettreReponse(session, round, config, track, "p1", round.Id, "a", round.DebutRound!.Value);
        _service.TerminerParTimeout(session, round, config);

        Assert.False(round.Reponses.Single(r => r.PlayerId == "p1").EstAbsent);
        Assert.True(round.Reponses.Single(r => r.PlayerId == "p2").EstAbsent);
    }

    // ----- V2, section 12.5 : cible Année -----

    [Fact]
    public void DemarrerRound_MorceauSansAnnee_CibleAnneeNestJamaisTiree()
    {
        var track = NouveauTrack("a"); // Year non renseigné
        var config = new GameConfig();

        for (var i = 0; i < 50; i++)
        {
            var round = new Round { TrackId = track.Id, Mode = RoundMode.TapeReponse };
            _service.DemarrerRound(round, track, [track], tags: [], config, DateTimeOffset.UtcNow);
            Assert.NotEqual(RoundCible.Annee, round.Cible);
        }
    }

    [Fact]
    public void DemarrerRound_MorceauAvecAnnee_PoidsAnneeAcentPourcent_ToujoursAnnee()
    {
        var track = new Track { Id = "a", Title = "T", Artist = "Artiste", FilePath = "audio/a.mp3", Year = 1990 };
        var config = new GameConfig { PoidsCibleTitre = 0, PoidsCibleAuteur = 0, PoidsCibleAnnee = 1 };

        for (var i = 0; i < 20; i++)
        {
            var round = new Round { TrackId = track.Id, Mode = RoundMode.TapeReponse };
            _service.DemarrerRound(round, track, [track], tags: [], config, DateTimeOffset.UtcNow);
            Assert.Equal(RoundCible.Annee, round.Cible);
        }
    }

    [Fact]
    public void DemarrerRound_MorceauDisney_ResteFilmMemeAvecAnneeConnue()
    {
        var track = new Track { Id = "a", Title = "T", Artist = "Artiste", FilePath = "audio/a.mp3", Year = 1990, Tags = ["disney"] };
        var config = new GameConfig { PoidsCibleAnnee = 100 };
        var round = new Round { TrackId = track.Id, Mode = RoundMode.TapeReponse };

        _service.DemarrerRound(round, track, [track], tags: [], config, DateTimeOffset.UtcNow);

        Assert.Equal(RoundCible.Film, round.Cible);
    }

    [Fact]
    public void DemarrerRound_CibleAnneeModePremiereLettre_BasculeVersTapeReponse()
    {
        var track = new Track { Id = "a", Title = "T", Artist = "Artiste", FilePath = "audio/a.mp3", Year = 1990 };
        var config = new GameConfig { PoidsCibleTitre = 0, PoidsCibleAuteur = 0, PoidsCibleAnnee = 1 };
        var round = new Round { TrackId = track.Id, Mode = RoundMode.PremiereLettre };

        _service.DemarrerRound(round, track, [track], tags: [], config, DateTimeOffset.UtcNow);

        Assert.Equal(RoundCible.Annee, round.Cible);
        Assert.Equal(RoundMode.TapeReponse, round.Mode);
    }

    [Fact]
    public void DemarrerRound_CibleAnneeModeQcm_GenereQuatreAnneesDontLaBonne()
    {
        var track = new Track { Id = "a", Title = "T", Artist = "Artiste", FilePath = "audio/a.mp3", Year = 1990 };
        var config = new GameConfig { PoidsCibleTitre = 0, PoidsCibleAuteur = 0, PoidsCibleAnnee = 1 };
        var round = new Round { TrackId = track.Id, Mode = RoundMode.Qcm };

        _service.DemarrerRound(round, track, [track], tags: [], config, DateTimeOffset.UtcNow);

        Assert.Equal(RoundCible.Annee, round.Cible);
        Assert.NotNull(round.AnneeOptions);
        Assert.Equal(4, round.AnneeOptions!.Count);
        Assert.Contains(1990, round.AnneeOptions);
        Assert.Null(round.QcmOptionTrackIds); // pas d'options "morceau" pour une question Année
    }

    [Fact]
    public void SoumettreReponse_CibleAnnee_EcartNul_CreditePleinPointsEnJeu()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = new Track { Id = "a", Title = "T", Artist = "Artiste", FilePath = "audio/a.mp3", Year = 1990 };
        var round = new Round { TrackId = "a", Mode = RoundMode.TapeReponse, Cible = RoundCible.Annee, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "1990", round.DebutRound!.Value);

        Assert.True(reponse!.EstCorrecte);
        Assert.Equal(0, reponse.EcartAnnee);
        Assert.Equal(100, reponse.Points);
    }

    [Fact]
    public void SoumettreReponse_CibleAnnee_EcartDansLaTolerance_CreditePartiellement()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = new Track { Id = "a", Title = "T", Artist = "Artiste", FilePath = "audio/a.mp3", Year = 1990 };
        var round = new Round { TrackId = "a", Mode = RoundMode.TapeReponse, Cible = RoundCible.Annee, DebutRound = DateTimeOffset.UtcNow };
        var config = NouveauConfig(); // ToleranceAnnee = 3 par défaut

        // écart 1 sur pointsEnJeu=100 -> round(100 * (1 - 1/4)) = 75
        var reponse = _service.SoumettreReponse(session, round, config, track, "p1", round.Id, "1991", round.DebutRound!.Value);

        Assert.True(reponse!.EstCorrecte); // écart <= ToleranceAnnee
        Assert.Equal(1, reponse.EcartAnnee);
        Assert.Equal(75, reponse.Points);
    }

    [Fact]
    public void SoumettreReponse_CibleAnnee_EcartAuDelaDeLaTolerance_PenaliteHabituelle()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = new Track { Id = "a", Title = "T", Artist = "Artiste", FilePath = "audio/a.mp3", Year = 1990 };
        var round = new Round { TrackId = "a", Mode = RoundMode.TapeReponse, Cible = RoundCible.Annee, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "1980", round.DebutRound!.Value);

        Assert.False(reponse!.EstCorrecte);
        Assert.Equal(10, reponse.EcartAnnee);
        Assert.Equal(-50, reponse.Points); // pénalité habituelle : -round(100 * 0.5)
    }

    [Fact]
    public void SoumettreReponse_CibleAnnee_SaisieNonNumerique_EstMauvaiseReponseSansEcart()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = new Track { Id = "a", Title = "T", Artist = "Artiste", FilePath = "audio/a.mp3", Year = 1990 };
        var round = new Round { TrackId = "a", Mode = RoundMode.TapeReponse, Cible = RoundCible.Annee, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "pas un nombre", round.DebutRound!.Value);

        Assert.False(reponse!.EstCorrecte);
        Assert.Null(reponse.EcartAnnee);
        Assert.Equal(-50, reponse.Points);
    }

    [Fact]
    public void SoumettreReponse_CibleAnneeModeQcm_CompareLeTexteDeLAnnee()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = new Track { Id = "a", Title = "T", Artist = "Artiste", FilePath = "audio/a.mp3", Year = 1990 };
        var round = new Round { TrackId = "a", Mode = RoundMode.Qcm, Cible = RoundCible.Annee, DebutRound = DateTimeOffset.UtcNow, AnneeOptions = [1985, 1990, 1995, 2000] };

        var bonneReponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", round.Id, "1990", round.DebutRound!.Value);

        Assert.True(bonneReponse!.EstCorrecte);
        Assert.Null(bonneReponse.OptionChoisieTrackId); // pas d'"option" RoundOption pour une année
    }
}
