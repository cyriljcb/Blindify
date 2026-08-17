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
    private readonly RoundService _service = new(new ScoringService(), new QcmGenerator(), new AnswerMatcher());

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
        Players = joueurs.ToList()
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
    public void SelectionnerMorceaux_PoolTagInsuffisant_CompleteAvecLePoolGlobal()
    {
        var pool = new List<Track> { NouveauTrack("niche", tags: ["niche"]), NouveauTrack("a"), NouveauTrack("b"), NouveauTrack("c") };
        var dejaUtilises = new HashSet<string>();

        var resultat = _service.SelectionnerMorceaux(pool, tags: ["niche"], nombre: 3, dejaUtilises);

        Assert.Equal(3, resultat.Count);
    }

    [Fact]
    public void DemarrerRound_ModeQcm_GenereQuatreOptions()
    {
        var correct = NouveauTrack("a", genres: ["pop"]);
        var catalogue = new List<Track> { correct, NouveauTrack("b", genres: ["pop"]), NouveauTrack("c", genres: ["pop"]), NouveauTrack("d", genres: ["pop"]) };
        var config = new GameConfig { ProbabiliteQcmPiege = 0 };
        var round = new Round { TrackId = correct.Id, Mode = RoundMode.Qcm };

        _service.DemarrerRound(round, correct, catalogue, config, DateTimeOffset.UtcNow);

        Assert.NotNull(round.QcmOptionTrackIds);
        Assert.Equal(4, round.QcmOptionTrackIds!.Count);
        Assert.Contains(correct.Id, round.QcmOptionTrackIds);
        Assert.NotNull(round.DebutRound);
    }

    [Fact]
    public void DemarrerRound_ModeTapeReponse_NeGenereAucuneOption()
    {
        var correct = NouveauTrack("a");
        var round = new Round { TrackId = correct.Id, Mode = RoundMode.TapeReponse };

        _service.DemarrerRound(round, correct, [correct], new GameConfig(), DateTimeOffset.UtcNow);

        Assert.Null(round.QcmOptionTrackIds);
    }

    [Fact]
    public void SoumettreReponse_BonneReponseQcm_CrediteLeJoueur()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = NouveauTrack("a");
        var round = new Round { TrackId = "a", Mode = RoundMode.Qcm, DebutRound = DateTimeOffset.UtcNow, QcmOptionTrackIds = ["a", "b", "c", "d"] };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", "a", round.DebutRound!.Value);

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

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", "b", round.DebutRound!.Value);

        Assert.False(reponse!.EstCorrecte);
        Assert.Equal(-50, reponse.Points);
        Assert.Equal(-50, joueur.Score);
    }

    [Fact]
    public void SoumettreReponse_PremiereLettreCorrecte_EstValidee()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = NouveauTrack("a"); // Title = "Titre a"
        var round = new Round { TrackId = "a", Mode = RoundMode.PremiereLettre, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", "t", round.DebutRound!.Value);

        Assert.True(reponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_PremiereLettreCasseDifferente_EstValidee()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = NouveauTrack("a");
        var round = new Round { TrackId = "a", Mode = RoundMode.PremiereLettre, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", "T", round.DebutRound!.Value);

        Assert.True(reponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_PremiereLettreAccentuee_EstNormaliseeAvantComparaison()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = new Track { Id = "a", Title = "Étoile", Artist = "Artiste", FilePath = "audio/a.mp3" };
        var round = new Round { TrackId = "a", Mode = RoundMode.PremiereLettre, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", "e", round.DebutRound!.Value);

        Assert.True(reponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_PremiereLettreIncorrecte_EstRefusee()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = NouveauTrack("a");
        var round = new Round { TrackId = "a", Mode = RoundMode.PremiereLettre, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", "x", round.DebutRound!.Value);

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
            _service.DemarrerRound(round, correct, [correct], new GameConfig(), DateTimeOffset.UtcNow);
            cibles.Add(round.Cible);
        }

        Assert.Contains(RoundCible.Titre, cibles);
        Assert.Contains(RoundCible.Auteur, cibles);
    }

    [Fact]
    public void SoumettreReponse_CibleAuteur_UnSeulDesPlusieursAuteursSuffit()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = new Track { Id = "a", Title = "Gone Gone Gone", Artist = "David Guetta, Tones And I, Teddy Swims", FilePath = "audio/a.mp3" };
        var round = new Round { TrackId = "a", Mode = RoundMode.TapeReponse, Cible = RoundCible.Auteur, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", "Teddy Swims", round.DebutRound!.Value);

        Assert.True(reponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_CibleAuteur_LeTitreNestPasAccepte()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = new Track { Id = "a", Title = "Gone Gone Gone", Artist = "David Guetta, Tones And I, Teddy Swims", FilePath = "audio/a.mp3" };
        var round = new Round { TrackId = "a", Mode = RoundMode.TapeReponse, Cible = RoundCible.Auteur, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", "Gone Gone Gone", round.DebutRound!.Value);

        Assert.False(reponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_CibleAuteurPremiereLettre_MatchNimporteLequelDesAuteurs()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = new Track { Id = "a", Title = "Gone Gone Gone", Artist = "David Guetta, Tones And I, Teddy Swims", FilePath = "audio/a.mp3" };
        var round = new Round { TrackId = "a", Mode = RoundMode.PremiereLettre, Cible = RoundCible.Auteur, DebutRound = DateTimeOffset.UtcNow };

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", "t", round.DebutRound!.Value);

        Assert.True(reponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_DeuxiemeEssaiDuMemeJoueur_EstRejete()
    {
        var joueur = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(joueur);
        var track = NouveauTrack("a");
        var round = new Round { TrackId = "a", Mode = RoundMode.Qcm, DebutRound = DateTimeOffset.UtcNow, QcmOptionTrackIds = ["a", "b", "c", "d"] };

        _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", "a", round.DebutRound!.Value);
        var deuxieme = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", "b", round.DebutRound!.Value);

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

        var reponse = _service.SoumettreReponse(session, round, NouveauConfig(), track, "p1", "a", round.DebutRound!.Value);

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

        _service.SoumettreReponse(session, round, config, track, "p1", "a", round.DebutRound!.Value);
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
        _service.SoumettreReponse(session, round, config, track, "p1", "Reponse hors tolerance", round.DebutRound!.Value);
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
}
