using Blindify.Application.Answers;
using Blindify.Application.Bonus;
using Blindify.Application.Scoring;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;
using Blindify.Domain.Enums;

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
        Players = joueurs.ToList(),
        HostSecret = "test-secret"
    };

    private static SeriesConfig NouveauConfig() => new()
    {
        PaliersDeMise = [10, 20, 30, 50]
    };

    [Fact]
    public void EnregistrerMise_PremiereFois_Reussit()
    {
        var session = NouvelleSession(new Player { PlayerId = "p1", Nom = "Alice" });
        var bonusRound = _service.CreerBonusRound(NouveauTrack());

        var succes = _service.EnregistrerMise(session, bonusRound, "p1", 1);

        Assert.True(succes);
        Assert.Single(bonusRound.Mises);
    }

    [Fact]
    public void EnregistrerMise_DeuxiemeMiseDuMemeJoueur_Echoue()
    {
        var session = NouvelleSession(new Player { PlayerId = "p1", Nom = "Alice" });
        var bonusRound = _service.CreerBonusRound(NouveauTrack());
        _service.EnregistrerMise(session, bonusRound, "p1", 1);

        var succes = _service.EnregistrerMise(session, bonusRound, "p1", 2);

        Assert.False(succes);
        Assert.Single(bonusRound.Mises);
    }

    [Fact]
    public void EnregistrerMise_ApresDebutPhaseQuestion_Echoue()
    {
        var session = NouvelleSession(new Player { PlayerId = "p1", Nom = "Alice" });
        var bonusRound = _service.CreerBonusRound(NouveauTrack());
        _service.DemarrerPhaseQuestion(bonusRound, DateTimeOffset.UtcNow);

        var succes = _service.EnregistrerMise(session, bonusRound, "p1", 1);

        Assert.False(succes);
    }

    [Fact]
    public void EnregistrerMise_PendantLaPause_Echoue()
    {
        // Retour utilisateur/skill (blindify-rules) : toute soumission reçue pendant enPause = true
        // doit être rejetée côté serveur, pas seulement filtrée côté client — voir SoumettreReponse
        // (round classique et bonus) qui appliquent déjà cette règle.
        var session = NouvelleSession(new Player { PlayerId = "p1", Nom = "Alice" });
        session.EnPause = true;
        var bonusRound = _service.CreerBonusRound(NouveauTrack());

        var succes = _service.EnregistrerMise(session, bonusRound, "p1", 1);

        Assert.False(succes);
        Assert.Empty(bonusRound.Mises);
    }

    [Fact]
    public void AppliquerPaliersParDefaut_NeTouchePasAuxJoueursAyantDejaMise()
    {
        var alice = new Player { PlayerId = "p1", Nom = "Alice" };
        var bob = new Player { PlayerId = "p2", Nom = "Bob" };
        var session = NouvelleSession(alice, bob);
        var bonusRound = _service.CreerBonusRound(NouveauTrack());
        _service.EnregistrerMise(session, bonusRound, "p1", 3);

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
        bonusRound.Cible = RoundCible.Titre; // cible tirée aléatoirement depuis le retour utilisateur du 2026-08-24 — fixée ici pour isoler le scoring testé
        _service.EnregistrerMise(session, bonusRound, "p1", 2); // palier 30
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
        _service.EnregistrerMise(session, bonusRound, "p1", 2); // palier 30
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
        bonusRound.Cible = RoundCible.Titre; // cible tirée aléatoirement depuis le retour utilisateur du 2026-08-24 — fixée ici pour isoler le comportement testé
        _service.EnregistrerMise(session, bonusRound, "p1", 0); // palier 10
        _service.EnregistrerMise(session, bonusRound, "p2", 3); // palier 50
        _service.DemarrerPhaseQuestion(bonusRound, DateTimeOffset.UtcNow);

        _service.SoumettreReponse(session, bonusRound, NouveauConfig(), track, "p1", "Under the Sea", DateTimeOffset.UtcNow);
        _service.TerminerParTimeout(session, bonusRound, NouveauConfig());

        Assert.Equal(10, alice.Score);
        Assert.Equal(-50, bob.Score);
    }

    [Fact]
    public void CreerBonusRound_MorceauDisney_CibleFilm()
    {
        var track = new Track { Id = "a", Title = "Under the Sea", Artist = "Samuel E. Wright", FilePath = "audio/a.mp3", Tags = ["disney"] };

        var bonusRound = _service.CreerBonusRound(track);

        Assert.Equal(RoundCible.Film, bonusRound.Cible);
    }

    [Fact]
    public void CreerBonusRound_MorceauNonDisney_CibleTitreOuAuteur()
    {
        // Tirage 50/50 depuis le retour utilisateur du 2026-08-24 (auparavant toujours Titre) — voir
        // BonusRoundService.CreerBonusRound. Pas d'assertion déterministe possible sur Random.Shared,
        // donc on vérifie seulement l'exclusion de Film pour un morceau non-Disney.
        var bonusRound = _service.CreerBonusRound(NouveauTrack());

        Assert.NotEqual(RoundCible.Film, bonusRound.Cible);
    }

    [Fact]
    public void CreerBonusRound_TitreTropLong_CibleTombeToujoursSurAuteur()
    {
        // Voir TitreVariantes.EstEligibleCommeCible : au-delà de 7 mots, la cible Titre est exclue
        // du tirage — déterministe, contrairement au test 50/50 ci-dessus.
        var track = new Track
        {
            Id = "a",
            Title = "Un Titre Avec Beaucoup Trop De Mots Dedans",
            Artist = "Un Artiste",
            FilePath = "audio/a.mp3"
        };

        var bonusRound = _service.CreerBonusRound(track);

        Assert.Equal(RoundCible.Auteur, bonusRound.Cible);
    }

    [Fact]
    public void SoumettreReponse_CibleFilm_CompareAuNomDuFilmPasAuTitreReel()
    {
        var alice = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(alice);
        var track = new Track
        {
            Id = "a",
            Title = "If I Didn't Have You",
            Artist = "Billy Crystal, John Goodman",
            Album = "Monsters, Inc. (Original Motion Picture Soundtrack)",
            FilePath = "audio/a.mp3",
            Tags = ["disney"]
        };
        var bonusRound = _service.CreerBonusRound(track);
        _service.EnregistrerMise(session, bonusRound, "p1", 2); // palier 30
        _service.DemarrerPhaseQuestion(bonusRound, DateTimeOffset.UtcNow);

        var reponseAvecTitreReel = _service.SoumettreReponse(session, bonusRound, NouveauConfig(), track, "p1", "If I Didn't Have You", DateTimeOffset.UtcNow);
        Assert.False(reponseAvecTitreReel!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_CibleTitre_ReponseSansLeContenuEntreParentheses_EstAcceptee()
    {
        var alice = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(alice);
        var track = new Track { Id = "a", Title = "Sweat (A La La La La Long)", Artist = "Inner Circle", FilePath = "audio/a.mp3" };
        var bonusRound = _service.CreerBonusRound(track);
        bonusRound.Cible = RoundCible.Titre; // cible tirée aléatoirement depuis le retour utilisateur du 2026-08-24 — fixée ici pour isoler le comportement testé
        _service.EnregistrerMise(session, bonusRound, "p1", 2); // palier 30
        _service.DemarrerPhaseQuestion(bonusRound, DateTimeOffset.UtcNow);

        var reponse = _service.SoumettreReponse(session, bonusRound, NouveauConfig(), track, "p1", "Sweat", DateTimeOffset.UtcNow);

        Assert.True(reponse!.EstCorrecte);
    }
}
