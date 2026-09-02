using Blindify.Application.Answers;
using Blindify.Application.Bonus;
using Blindify.Application.Qcm;
using Blindify.Application.Scoring;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;
using Blindify.Domain.Enums;

namespace Blindify.Tests.Bonus;

public class BonusRoundServiceTests
{
    private readonly BonusRoundService _service = new(new BonusScoringService(), new AnswerMatcher(), new QcmGenerator());

    private static Track NouveauTrack(string id = "a") => new()
    {
        Id = id,
        Title = "Under the Sea",
        Artist = "Samuel E. Wright",
        FilePath = $"audio/{id}.mp3"
    };

    // Catalogue/tags/config minimaux : suffisants pour les tests ci-dessous, qui ne portent pas
    // sur la génération QCM elle-même (voir BonusRoundServiceTests_Modes) — Mode forcé à
    // TapeReponse juste après quand le test dépend de la comparaison texte.
    // ProbabiliteBonusCourse à 0 par défaut : la plupart des tests de ce fichier vérifient des
    // pénalités/scores incompatibles avec le mode course (retour utilisateur — un tirage à 0.5
    // rendait TerminerParTimeout_PenaliseLesJoueursAyantMiseSansRepondre flaky, cf. même correctif
    // déjà appliqué à GameHubBonusIntegrationTests). Les tests dédiés au mode course passent leur
    // propre config directement à _service.CreerBonusRound, sans passer par ce helper.
    private BonusRound CreerBonusRound(Track track) => _service.CreerBonusRound(track, [track], [], new GameConfig { ProbabiliteBonusCourse = 0 });

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
        var bonusRound = CreerBonusRound(NouveauTrack());

        var succes = _service.EnregistrerMise(session, bonusRound, "p1", 1);

        Assert.True(succes);
        Assert.Single(bonusRound.Mises);
    }

    [Fact]
    public void EnregistrerMise_DeuxiemeMiseDuMemeJoueur_Echoue()
    {
        var session = NouvelleSession(new Player { PlayerId = "p1", Nom = "Alice" });
        var bonusRound = CreerBonusRound(NouveauTrack());
        _service.EnregistrerMise(session, bonusRound, "p1", 1);

        var succes = _service.EnregistrerMise(session, bonusRound, "p1", 2);

        Assert.False(succes);
        Assert.Single(bonusRound.Mises);
    }

    [Fact]
    public void EnregistrerMise_ApresDebutPhaseQuestion_Echoue()
    {
        var session = NouvelleSession(new Player { PlayerId = "p1", Nom = "Alice" });
        var bonusRound = CreerBonusRound(NouveauTrack());
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
        var bonusRound = CreerBonusRound(NouveauTrack());

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
        var bonusRound = CreerBonusRound(NouveauTrack());
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
        var bonusRound = CreerBonusRound(track);
        bonusRound.Cible = RoundCible.Titre; // cible tirée aléatoirement depuis le retour utilisateur du 2026-08-24 — fixée ici pour isoler le scoring testé
        bonusRound.Mode = RoundMode.TapeReponse; // Mode tiré aléatoirement depuis le 2026-08-27 — fixé ici pour isoler le scoring testé
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
        var bonusRound = CreerBonusRound(track);
        bonusRound.Mode = RoundMode.TapeReponse; // Mode tiré aléatoirement depuis le 2026-08-27 — fixé ici pour isoler le scoring testé
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
        var bonusRound = CreerBonusRound(track);
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
        var bonusRound = CreerBonusRound(track);
        bonusRound.Cible = RoundCible.Titre; // cible tirée aléatoirement depuis le retour utilisateur du 2026-08-24 — fixée ici pour isoler le comportement testé
        bonusRound.Mode = RoundMode.TapeReponse; // Mode tiré aléatoirement depuis le 2026-08-27 — fixé ici pour isoler le comportement testé
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

        var bonusRound = CreerBonusRound(track);

        Assert.Equal(RoundCible.Film, bonusRound.Cible);
    }

    [Fact]
    public void CreerBonusRound_MorceauNonDisney_CibleTitreOuAuteur()
    {
        // Tirage 50/50 depuis le retour utilisateur du 2026-08-24 (auparavant toujours Titre) — voir
        // BonusRoundService.CreerBonusRound. Pas d'assertion déterministe possible sur Random.Shared,
        // donc on vérifie seulement l'exclusion de Film pour un morceau non-Disney.
        var bonusRound = CreerBonusRound(NouveauTrack());

        Assert.NotEqual(RoundCible.Film, bonusRound.Cible);
    }

    [Fact]
    public void CreerBonusRound_TitreTropLong_CibleTombeToujoursSurAuteur()
    {
        // Voir TitreVariantes.EstEligibleCommeCible : au-delà de 35 caractères, la cible Titre est
        // exclue du tirage — déterministe, contrairement au test 50/50 ci-dessus.
        var track = new Track
        {
            Id = "a",
            Title = "Un Titre Avec Beaucoup Trop De Mots Dedans",
            Artist = "Un Artiste",
            FilePath = "audio/a.mp3"
        };

        var bonusRound = CreerBonusRound(track);

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
        var bonusRound = CreerBonusRound(track);
        bonusRound.Mode = RoundMode.TapeReponse; // Mode tiré aléatoirement depuis le 2026-08-27 — fixé ici pour isoler le comportement testé
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
        var bonusRound = CreerBonusRound(track);
        bonusRound.Cible = RoundCible.Titre; // cible tirée aléatoirement depuis le retour utilisateur du 2026-08-24 — fixée ici pour isoler le comportement testé
        bonusRound.Mode = RoundMode.TapeReponse; // Mode tiré aléatoirement depuis le 2026-08-27 — fixé ici pour isoler le comportement testé
        _service.EnregistrerMise(session, bonusRound, "p1", 2); // palier 30
        _service.DemarrerPhaseQuestion(bonusRound, DateTimeOffset.UtcNow);

        var reponse = _service.SoumettreReponse(session, bonusRound, NouveauConfig(), track, "p1", "Sweat", DateTimeOffset.UtcNow);

        Assert.True(reponse!.EstCorrecte);
    }

    [Fact]
    public void CreerBonusRound_TirageModeSur300Essais_LesTroisModesApparaissent()
    {
        // Retour utilisateur 2026-08-27 : la question bonus se limitait à TapeReponse (jamais
        // QCM/Première lettre, contrairement aux rounds classiques) — voir tirage aléatoire dans
        // BonusRoundService.CreerBonusRound. Pas d'assertion déterministe possible sur
        // Random.Shared, donc on vérifie juste que les 3 modes finissent par sortir.
        var modes = Enumerable.Range(0, 300).Select(_ => CreerBonusRound(NouveauTrack()).Mode).Distinct().ToList();

        Assert.Contains(RoundMode.Qcm, modes);
        Assert.Contains(RoundMode.TapeReponse, modes);
        Assert.Contains(RoundMode.PremiereLettre, modes);
    }

    [Fact]
    public void CreerBonusRound_ModeQcm_GenereQuatreOptionsDepuisLeCatalogue()
    {
        var track = NouveauTrack("a");
        var catalogue = new List<Track>
        {
            track,
            new() { Id = "b", Title = "B", Artist = "Artiste B", FilePath = "audio/b.mp3" },
            new() { Id = "c", Title = "C", Artist = "Artiste C", FilePath = "audio/c.mp3" },
            new() { Id = "d", Title = "D", Artist = "Artiste D", FilePath = "audio/d.mp3" },
            new() { Id = "e", Title = "E", Artist = "Artiste E", FilePath = "audio/e.mp3" },
        };

        BonusRound? bonusRound = null;
        for (var i = 0; i < 100 && bonusRound?.Mode != RoundMode.Qcm; i++)
            bonusRound = _service.CreerBonusRound(track, catalogue, [], new GameConfig());

        Assert.Equal(RoundMode.Qcm, bonusRound!.Mode);
        Assert.NotNull(bonusRound.QcmOptionTrackIds);
        Assert.Equal(4, bonusRound.QcmOptionTrackIds!.Count);
        Assert.Contains(track.Id, bonusRound.QcmOptionTrackIds);
    }

    [Fact]
    public void SoumettreReponse_ModeQcm_CompareLIdDuMorceauPasLeTexte()
    {
        var alice = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(alice);
        var track = NouveauTrack();
        var bonusRound = CreerBonusRound(track);
        bonusRound.Mode = RoundMode.Qcm; // tiré aléatoirement — fixé ici pour isoler le comportement testé
        _service.EnregistrerMise(session, bonusRound, "p1", 2); // palier 30
        _service.DemarrerPhaseQuestion(bonusRound, DateTimeOffset.UtcNow);

        var bonneReponse = _service.SoumettreReponse(session, bonusRound, NouveauConfig(), track, "p1", track.Id, DateTimeOffset.UtcNow);

        Assert.True(bonneReponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_ModeQcm_CibleAuteur_DeuxOptionsMemeAuteur_LesDeuxSontAcceptees()
    {
        // Même correctif que RoundService (retour utilisateur : "Myles Smith" en double dans un
        // QCM, filet de sécurité de QcmGenerator sur un thème trop restreint) — même repli par
        // libellé affiché pour la question bonus.
        var alice = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(alice);
        var correct = new Track { Id = "a", Title = "Stargazing", Artist = "Myles Smith", FilePath = "audio/a.mp3" };
        var doublon = new Track { Id = "b", Title = "Nice To Meet You", Artist = "Myles Smith", FilePath = "audio/b.mp3" };
        var catalogue = new[] { correct, doublon }.ToDictionary(t => t.Id);
        var bonusRound = CreerBonusRound(correct);
        bonusRound.Cible = RoundCible.Auteur;
        bonusRound.Mode = RoundMode.Qcm;
        _service.EnregistrerMise(session, bonusRound, "p1", 2);
        _service.DemarrerPhaseQuestion(bonusRound, DateTimeOffset.UtcNow);

        var reponse = _service.SoumettreReponse(session, bonusRound, NouveauConfig(), correct, "p1", "b", DateTimeOffset.UtcNow, id => catalogue.GetValueOrDefault(id));

        Assert.True(reponse!.EstCorrecte);
    }

    [Fact]
    public void SoumettreReponse_ModePremiereLettre_CompareLaPremiereLettreDuTitre()
    {
        var alice = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(alice);
        var track = NouveauTrack(); // "Under the Sea"
        var bonusRound = CreerBonusRound(track);
        bonusRound.Cible = RoundCible.Titre;
        bonusRound.Mode = RoundMode.PremiereLettre; // tiré aléatoirement — fixé ici pour isoler le comportement testé
        _service.EnregistrerMise(session, bonusRound, "p1", 2); // palier 30
        _service.DemarrerPhaseQuestion(bonusRound, DateTimeOffset.UtcNow);

        var bonneReponse = _service.SoumettreReponse(session, bonusRound, NouveauConfig(), track, "p1", "U", DateTimeOffset.UtcNow);

        Assert.True(bonneReponse!.EstCorrecte);
    }

    // ----- Mode "course" (retour utilisateur) : premier qui répond, juste ou faux, décide seul —
    // réservé au Mode.Qcm, probabilité configurable (GameConfig.ProbabiliteBonusCourse). -----

    [Fact]
    public void CreerBonusRound_ModeQcmProbabiliteCourseA1_EstCourseVrai()
    {
        var config = new GameConfig { ProbabiliteBonusCourse = 1.0 };
        var track = NouveauTrack();

        BonusRound? bonusRound = null;
        for (var i = 0; i < 100 && bonusRound?.Mode != RoundMode.Qcm; i++)
            bonusRound = _service.CreerBonusRound(track, [track], [], config);

        Assert.Equal(RoundMode.Qcm, bonusRound!.Mode);
        Assert.True(bonusRound.EstCourse);
    }

    [Fact]
    public void CreerBonusRound_ModeQcmProbabiliteCourseA0_EstCourseFaux()
    {
        var config = new GameConfig { ProbabiliteBonusCourse = 0.0 };
        var track = NouveauTrack();

        BonusRound? bonusRound = null;
        for (var i = 0; i < 100 && bonusRound?.Mode != RoundMode.Qcm; i++)
            bonusRound = _service.CreerBonusRound(track, [track], [], config);

        Assert.Equal(RoundMode.Qcm, bonusRound!.Mode);
        Assert.False(bonusRound.EstCourse);
    }

    [Fact]
    public void CreerBonusRound_ModeNonQcm_EstCourseToujoursFauxMemeAvecProbabiliteA1()
    {
        // "Seulement dispo en QCM" (retour utilisateur) : la probabilité ne doit jamais s'appliquer
        // à TapeReponse/PremiereLettre, même à 100%.
        var config = new GameConfig { ProbabiliteBonusCourse = 1.0 };
        var track = NouveauTrack();

        BonusRound bonusRound;
        var tentative = 0;
        do
        {
            bonusRound = _service.CreerBonusRound(track, [track], [], config);
            tentative++;
        } while (bonusRound.Mode == RoundMode.Qcm && tentative < 100);

        Assert.NotEqual(RoundMode.Qcm, bonusRound.Mode);
        Assert.False(bonusRound.EstCourse);
    }

    [Fact]
    public void SoumettreReponse_Course_DeuxiemeReponseApresLaPremiereEstIgnoree()
    {
        var alice = new Player { PlayerId = "p1", Nom = "Alice" };
        var bob = new Player { PlayerId = "p2", Nom = "Bob" };
        var session = NouvelleSession(alice, bob);
        var track = NouveauTrack();
        var bonusRound = CreerBonusRound(track);
        bonusRound.Mode = RoundMode.Qcm;
        bonusRound.EstCourse = true;
        _service.EnregistrerMise(session, bonusRound, "p1", 2); // palier 30
        _service.EnregistrerMise(session, bonusRound, "p2", 3); // palier 50
        _service.DemarrerPhaseQuestion(bonusRound, DateTimeOffset.UtcNow);

        var premiereReponse = _service.SoumettreReponse(session, bonusRound, NouveauConfig(), track, "p1", "autre-id", DateTimeOffset.UtcNow);
        var deuxiemeReponse = _service.SoumettreReponse(session, bonusRound, NouveauConfig(), track, "p2", track.Id, DateTimeOffset.UtcNow);

        Assert.NotNull(premiereReponse);
        Assert.False(premiereReponse!.EstCorrecte);
        Assert.Equal(-30, alice.Score);
        Assert.Null(deuxiemeReponse); // arrivé après la course déjà tranchée — ignoré
        Assert.Equal(0, bob.Score); // ni gagné ni perdu
    }

    [Fact]
    public void TerminerParTimeout_Course_NePenalisePasLesAutresSiQuelquUnADejaRepondu()
    {
        var alice = new Player { PlayerId = "p1", Nom = "Alice" };
        var bob = new Player { PlayerId = "p2", Nom = "Bob" };
        var session = NouvelleSession(alice, bob);
        var track = NouveauTrack();
        var bonusRound = CreerBonusRound(track);
        bonusRound.Mode = RoundMode.Qcm;
        bonusRound.EstCourse = true;
        _service.EnregistrerMise(session, bonusRound, "p1", 2); // palier 30
        _service.EnregistrerMise(session, bonusRound, "p2", 3); // palier 50
        _service.DemarrerPhaseQuestion(bonusRound, DateTimeOffset.UtcNow);

        _service.SoumettreReponse(session, bonusRound, NouveauConfig(), track, "p1", track.Id, DateTimeOffset.UtcNow);
        _service.TerminerParTimeout(session, bonusRound, NouveauConfig());

        Assert.Equal(30, alice.Score); // premier, correct : gagne sa mise
        Assert.Equal(0, bob.Score); // n'a pas eu l'occasion de répondre : ni gagné ni perdu
    }

    [Fact]
    public void TerminerParTimeout_Course_PenaliseToutLeMondeSiPersonneNaRepondu()
    {
        var alice = new Player { PlayerId = "p1", Nom = "Alice" };
        var session = NouvelleSession(alice);
        var track = NouveauTrack();
        var bonusRound = CreerBonusRound(track);
        bonusRound.Mode = RoundMode.Qcm;
        bonusRound.EstCourse = true;
        _service.EnregistrerMise(session, bonusRound, "p1", 2); // palier 30
        _service.DemarrerPhaseQuestion(bonusRound, DateTimeOffset.UtcNow);

        _service.TerminerParTimeout(session, bonusRound, NouveauConfig());

        Assert.Equal(-30, alice.Score); // aucune réponse du tout : comportement inchangé (perte de la mise)
    }
}
