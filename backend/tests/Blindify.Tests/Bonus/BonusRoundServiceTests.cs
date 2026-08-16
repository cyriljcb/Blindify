using Blindify.Application.Answers;
using Blindify.Application.Bonus;
using Blindify.Application.Scoring;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;

namespace Blindify.Tests.Bonus;

public class BonusRoundServiceTests
{
    private readonly BonusRoundService _service = new(new BonusScoringService(), new AnswerMatcher());

    private static Track NouveauTrack(string id = "a") => new()
    {
        Id = id,
        Title = "Under the Sea",
        Artist = "Samuel E. Wright",
        FilePath = $"audio/{id}.mp3"
    };

    private static GameSession NouvelleSession(params Player[] joueurs) => new()
    {
        Id = "ABCDE",
        Config = new GameConfig(),
        Players = joueurs.ToList()
    };

    private static SeriesConfig NouveauConfig() => new()
    {
        PaliersDeMise = [10, 20, 30, 50]
    };

    [Fact]
    public void EnregistrerMise_PremiereFois_Reussit()
    {
        var bonusRound = _service.CreerBonusRound(NouveauTrack());

        var succes = _service.EnregistrerMise(bonusRound, "p1", 1);

        Assert.True(succes);
        Assert.Single(bonusRound.Mises);
    }

    [Fact]
    public void EnregistrerMise_DeuxiemeMiseDuMemeJoueur_Echoue()
    {
        var bonusRound = _service.CreerBonusRound(NouveauTrack());
        _service.EnregistrerMise(bonusRound, "p1", 1);

        var succes = _service.EnregistrerMise(bonusRound, "p1", 2);

        Assert.False(succes);
        Assert.Single(bonusRound.Mises);
    }

    [Fact]
    public void EnregistrerMise_ApresDebutPhaseQuestion_Echoue()
    {
        var bonusRound = _service.CreerBonusRound(NouveauTrack());
        _service.DemarrerPhaseQuestion(bonusRound, DateTimeOffset.UtcNow);

        var succes = _service.EnregistrerMise(bonusRound, "p1", 1);

        Assert.False(succes);
    }

    [Fact]
    public void AppliquerPaliersParDefaut_NeTouchePasAuxJoueursAyantDejaMise()
    {
        var alice = new Player { PlayerId = "p1", Nom = "Alice" };
        var bob = new Player { PlayerId = "p2", Nom = "Bob" };
        var session = NouvelleSession(alice, bob);
        var bonusRound = _service.CreerBonusRound(NouveauTrack());
        _service.EnregistrerMise(bonusRound, "p1", 3);

        _service.AppliquerPaliersParDefaut(session, bonusRound);

        Assert.Equal(2, bonusRound.Mises.Count);
        Assert.Equal(3, bonusRound.Mises.Single(m => m.PlayerId == "p1").PalierIndex);
        Assert.Equal(0, bonusRound.Mises.Single(m => m.PlayerId == "p2").PalierIndex);
    }

    [Fact]
    public void SoumettreReponse_BonneReponse_GagneLaMise()
    {
        var alice = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(alice);
        var track = NouveauTrack();
        var bonusRound = _service.CreerBonusRound(track);
        _service.EnregistrerMise(bonusRound, "p1", 2); // palier 30
        _service.DemarrerPhaseQuestion(bonusRound, DateTimeOffset.UtcNow);

        var reponse = _service.SoumettreReponse(session, bonusRound, NouveauConfig(), track, "p1", "Under the Sea", DateTimeOffset.UtcNow);

        Assert.NotNull(reponse);
        Assert.True(reponse!.EstCorrecte);
        Assert.Equal(30, alice.Score);
    }

    [Fact]
    public void SoumettreReponse_MauvaiseReponse_PerdLaMise()
    {
        var alice = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(alice);
        var track = NouveauTrack();
        var bonusRound = _service.CreerBonusRound(track);
        _service.EnregistrerMise(bonusRound, "p1", 2); // palier 30
        _service.DemarrerPhaseQuestion(bonusRound, DateTimeOffset.UtcNow);

        var reponse = _service.SoumettreReponse(session, bonusRound, NouveauConfig(), track, "p1", "Autre Chose", DateTimeOffset.UtcNow);

        Assert.False(reponse!.EstCorrecte);
        Assert.Equal(-30, alice.Score);
    }

    [Fact]
    public void SoumettreReponse_SansAvoirMise_Echoue()
    {
        var alice = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(alice);
        var track = NouveauTrack();
        var bonusRound = _service.CreerBonusRound(track);
        _service.DemarrerPhaseQuestion(bonusRound, DateTimeOffset.UtcNow);

        var reponse = _service.SoumettreReponse(session, bonusRound, NouveauConfig(), track, "p1", "Under the Sea", DateTimeOffset.UtcNow);

        Assert.Null(reponse);
        Assert.Equal(0, alice.Score);
    }

    [Fact]
    public void TerminerParTimeout_PenaliseLesJoueursAyantMiseSansRepondre()
    {
        var alice = new Player { PlayerId = "p1", Nom = "Alice" };
        var bob = new Player { PlayerId = "p2", Nom = "Bob" };
        var session = NouvelleSession(alice, bob);
        var track = NouveauTrack();
        var bonusRound = _service.CreerBonusRound(track);
        _service.EnregistrerMise(bonusRound, "p1", 0); // palier 10
        _service.EnregistrerMise(bonusRound, "p2", 3); // palier 50
        _service.DemarrerPhaseQuestion(bonusRound, DateTimeOffset.UtcNow);

        _service.SoumettreReponse(session, bonusRound, NouveauConfig(), track, "p1", "Under the Sea", DateTimeOffset.UtcNow);
        _service.TerminerParTimeout(session, bonusRound, NouveauConfig());

        Assert.Equal(10, alice.Score);
        Assert.Equal(-50, bob.Score);
    }
}
