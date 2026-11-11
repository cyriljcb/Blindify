using System.Text.Json;
using Blindify.Application.Answers;
using Blindify.Application.Bonus;
using Blindify.Application.Jokers;
using Blindify.Application.Qcm;
using Blindify.Application.Rounds;
using Blindify.Application.Scoring;
using Blindify.Application.Sessions;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;
using Blindify.Domain.Enums;
using Blindify.Infrastructure.Persistence;

namespace Blindify.Tests.Rounds;

/// <summary>Non-régression des points relevés par l'audit du 2026-11-10.</summary>
public class AuditCorrectionsTests
{
    private readonly AnswerMatcher _matcher = new();
    private readonly RoundService _service = new(new ScoringService(), new QcmGenerator(), new AnswerMatcher(), new AnneeQcmGenerator(), new JokerService(new AnswerMatcher()));

    private static GameSession Session(params Player[] joueurs) => new()
    {
        Id = "ABCDE",
        Config = new GameConfig(),
        Players = [.. joueurs],
        HostSecret = "s",
    };

    private static SeriesConfig Config() => new()
    {
        DureeFenetreReponseMs = 10_000,
        PointsMax = 100,
        PointsMin = 20,
        PenaliteMauvaiseReponseRatio = 0.5,
        PenaliteAbsenceReponse = -5,
        PaliersDeMise = [10, 20, 30, 50],
    };

    // ----- Normalisation des réponses (retour utilisateur : Ø, &) -----

    [Theory]
    [InlineData("MØ", "mo")]
    [InlineData("Cœur De Pirate", "coeur de pirate")]
    [InlineData("Straße", "strasse")]
    [InlineData("Æon", "aeon")]
    public void Normaliser_LettresSansDecomposition_SontRameneesALAlphabetLatin(string texte, string attendu) =>
        Assert.Equal(attendu, _matcher.Normaliser(texte));

    [Theory]
    [InlineData("earth wind and fire")]
    [InlineData("Earth Wind et Fire")]
    [InlineData("earth, wind & fire")]
    [InlineData("Earth Wind Fire")]
    public void EstCorrecte_EsperluetteEquivautAEtOuAnd(string saisie) =>
        Assert.True(_matcher.EstCorrecte(saisie, "Earth, Wind & Fire", new GameConfig()));

    [Fact]
    public void EstCorrecte_SaisieSansOBarre_EstAcceptee() =>
        Assert.True(_matcher.EstCorrecte("Mo", "MØ", new GameConfig { LongueurMinimalePourToleranceFixe = 2 }));

    [Theory]
    [InlineData("Mike", "Muse")]
    [InlineData("ABBA", "Aqua")]
    [InlineData("Sti", "Sting")]
    public void EstCorrecte_TexteDeQuatreACinqLettres_UnSeulCaractereDEcartTolere(string saisie, string attendu) =>
        Assert.False(_matcher.EstCorrecte(saisie, attendu, new GameConfig()));

    [Fact]
    public void EstCorrecte_TexteDeSixLettres_GardeDeuxCaracteresDEcart() =>
        Assert.True(_matcher.EstCorrecte("Hayley", "Halsey", new GameConfig()));

    // ----- Artistes crédités -----

    [Fact]
    public void AuteurVariantes_ChampArtists_PrioritaireSurLeDecoupageParVirgule()
    {
        var track = new Track { Id = "x", Title = "September", Artist = "Earth, Wind & Fire", Artists = ["Earth, Wind & Fire"], FilePath = "x" };
        Assert.Equal(["Earth, Wind & Fire"], AuteurVariantes.Acceptables(track));
        Assert.Equal("Earth, Wind & Fire", AuteurVariantes.Premier(track));
    }

    [Fact]
    public void AuteurVariantes_SansChampArtists_DecoupeParVirgule()
    {
        var track = new Track { Id = "x", Title = "T", Artist = "Angèle, Roméo Elvis", FilePath = "x" };
        Assert.Equal(["Angèle", "Roméo Elvis"], AuteurVariantes.Acceptables(track));
    }

    // ----- QCM -----

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AnneeQcm_MorceauRecent_OptionsToujoursDistinctesEtJamaisDansLeFutur(int anneesAvantMaintenant)
    {
        var generator = new AnneeQcmGenerator();
        var annee = DateTime.UtcNow.Year - anneesAvantMaintenant;
        for (var graine = 0; graine < 200; graine++)
        {
            var options = generator.GenererOptions(annee, new Random(graine));
            Assert.Equal(4, options.Distinct().Count());
            Assert.Contains(annee, options);
            Assert.All(options, o => Assert.True(o <= DateTime.UtcNow.Year));
        }
    }

    [Fact]
    public void QcmGenerator_CibleFilm_JamaisDeuxOptionsAuMemeLibelle()
    {
        Track Disney(string id, string oeuvre) => new() { Id = id, Title = $"Chanson {id}", Artist = $"Artiste {id}", Oeuvre = oeuvre, FilePath = "x", Tags = ["disney"] };
        var correct = Disney("a", "Le Roi Lion");
        var pool = new List<Track> { correct, Disney("b", "Le Roi Lion"), Disney("c", "Le Roi Lion"), Disney("d", "Aladdin"), Disney("e", "Hercule"), Disney("f", "Mulan") };

        for (var graine = 0; graine < 100; graine++)
        {
            var options = new QcmGenerator().GenererOptions(correct, pool, new GameConfig { ProbabiliteQcmPiege = 0 }, new Random(graine), RoundCible.Film);
            var libelles = options.OptionsTrackIds.Select(id => FilmNameResolver.Resoudre(pool.First(t => t.Id == id))).ToList();
            Assert.Equal(libelles.Count, libelles.Distinct().Count());
            Assert.Contains("a", options.OptionsTrackIds);
        }
    }

    // ----- Cycle du round -----

    [Fact]
    public void SoumettreReponse_RoundDejaTermine_EstRefusee()
    {
        var alice = new Player { PlayerId = "p1", Nom = "Alice" };
        var bob = new Player { PlayerId = "p2", Nom = "Bob" };
        var session = Session(alice);
        var round = new Round { TrackId = "a", Mode = RoundMode.TapeReponse, Cible = RoundCible.Titre, DebutRound = DateTimeOffset.UtcNow };
        var track = new Track { Id = "a", Title = "Titre", Artist = "Artiste", FilePath = "x" };

        _service.TerminerParTimeout(session, round, Config());
        session.Players.Add(bob); // rejoint pendant le reveal

        Assert.Null(_service.SoumettreReponse(session, round, Config(), track, "p2", round.Id, "Titre", DateTimeOffset.UtcNow));
        Assert.Equal(0, bob.Score);
    }

    [Fact]
    public void SoumettreReponse_ReponseVide_NeConsommePasLEssai()
    {
        var alice = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = Session(alice);
        var round = new Round { TrackId = "a", Mode = RoundMode.TapeReponse, Cible = RoundCible.Titre, DebutRound = DateTimeOffset.UtcNow };
        var track = new Track { Id = "a", Title = "Titre", Artist = "Artiste", FilePath = "x" };

        Assert.Null(_service.SoumettreReponse(session, round, Config(), track, "p1", round.Id, "   ", DateTimeOffset.UtcNow));
        Assert.NotNull(_service.SoumettreReponse(session, round, Config(), track, "p1", round.Id, "Titre", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ValiderManuellement_JoueurAbsent_EstRefuse()
    {
        var alice = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = Session(alice);
        var round = new Round { TrackId = "a", Mode = RoundMode.TapeReponse, DebutRound = DateTimeOffset.UtcNow };
        _service.TerminerParTimeout(session, round, Config());

        Assert.Null(_service.ValiderManuellement(session, round, Config(), "p1", true));
        Assert.Equal(-5, alice.Score);
    }

    [Fact]
    public void DemarrerRound_FilmCommencantParUnChiffreEnPremiereLettre_BasculeEnSaisie()
    {
        var track = new Track { Id = "a", Title = "Lose Yourself", Artist = "Eminem", Oeuvre = "8 Mile", FilePath = "x", Tags = ["film-serie"] };
        var round = new Round { TrackId = "a", Mode = RoundMode.PremiereLettre };

        _service.DemarrerRound(round, track, [track], ["film-serie"], new GameConfig(), DateTimeOffset.UtcNow);

        Assert.Equal(RoundCible.Film, round.Cible);
        Assert.Equal(RoundMode.TapeReponse, round.Mode);
    }

    // ----- Question bonus -----

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void EnregistrerMise_PalierHorsBornes_EstRefusee(int palierIndex)
    {
        var service = new BonusRoundService(new BonusScoringService(), new AnswerMatcher(), new QcmGenerator(), new AnneeQcmGenerator());
        var session = Session(new Player { PlayerId = "p1", Nom = "Alice" });
        var bonus = new BonusRound { TrackId = "a", DebutPhaseMise = DateTimeOffset.UtcNow };

        Assert.False(service.EnregistrerMise(session, bonus, Config(), "p1", bonus.Id, palierIndex));
        Assert.Empty(bonus.Mises);
    }

    // ----- Configuration -----

    [Fact]
    public void Validator_FenetreDeReponseNulle_EstRefusee() =>
        Assert.NotNull(ConfigurationPartieValidator.Valider(new GameConfig(), 1, 5, 0, 100, 20, 0.5, -5, 15000, 20000));

    [Fact]
    public void Validator_PointsMinSuperieursAuxPointsMax_EstRefusee() =>
        Assert.NotNull(ConfigurationPartieValidator.Valider(new GameConfig(), 1, 5, 20000, 10, 50, 0.5, -5, 15000, 20000));

    [Fact]
    public void Validator_ConfigurationParDefaut_EstAcceptee() =>
        Assert.Null(ConfigurationPartieValidator.Valider(new GameConfig(), 3, 8, 20000, 100, 20, 0.5, -5, 15000, 20000));

    [Fact]
    public void PaliersPourSerie_UtiliseLesPaliersDeBaseConfigures() =>
        Assert.Equal([100, 200, 300, 500], SeriesPlanner.PaliersPourSerie(0, 1.6, [100, 200, 300, 500]));

    // ----- Fichiers runtime -----

    [Fact]
    public void AtomicJsonFileRead_FichierTronque_RepartDUnEtatVideEtGardeUneCopie()
    {
        var dossier = Path.Combine(Path.GetTempPath(), $"blindify-corrupt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dossier);
        try
        {
            var chemin = Path.Combine(dossier, "stats.json");
            File.WriteAllText(chemin, """{"t1":{"playCount":3""");

            var lu = AtomicJsonFile.Read<Dictionary<string, object>>(chemin, new JsonSerializerOptions(), () => []);

            Assert.Empty(lu);
            Assert.Single(Directory.GetFiles(dossier, "stats.json.corrupt-*"));
        }
        finally
        {
            Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public void AtomicJsonFileRead_FichierTronqueMaisTemporaireComplet_RelitLeTemporaire()
    {
        var dossier = Path.Combine(Path.GetTempPath(), $"blindify-corrupt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dossier);
        try
        {
            var chemin = Path.Combine(dossier, "flags.json");
            File.WriteAllText(chemin, "[{");
            File.WriteAllText(chemin + ".tmp", """["a","b"]""");

            var lu = AtomicJsonFile.Read<List<string>>(chemin, new JsonSerializerOptions(), () => []);

            Assert.Equal(["a", "b"], lu);
        }
        finally
        {
            Directory.Delete(dossier, recursive: true);
        }
    }
}
