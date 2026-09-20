using Blindify.Application.Awards;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;
using Blindify.Domain.Enums;

namespace Blindify.Tests.Awards;

public class TitresServiceTests
{
    private static GameSession CreerSession(params Player[] joueurs) => new()
    {
        Id = "g1",
        Config = new GameConfig(),
        HostSecret = "secret",
        Players = [.. joueurs],
        SeriesList = [],
    };

    private static Player CreerJoueur(string id, int score = 0) => new() { PlayerId = id, Nom = id, Score = score };

    private static Series NouvelleSerie(int index) => new() { Index = index, Config = new SeriesConfig() };

    private static Track? ResolveurVide(string _) => null;

    // ----- ECLAIR -----

    [Fact]
    public void Eclair_AttribueAuJoueurAuTempsMoyenLePlusFaibleSurLesBonnesReponses()
    {
        var p1 = CreerJoueur("p1");
        var p2 = CreerJoueur("p2");
        var session = CreerSession(p1, p2);
        var serie = NouvelleSerie(0);

        for (var i = 0; i < 3; i++)
            serie.Rounds.Add(new Round
            {
                TrackId = $"t{i}",
                Reponses =
                [
                    new RoundAnswer { PlayerId = "p1", Reponse = "x", EstCorrecte = true, TempsReponseMs = 100 },
                    new RoundAnswer { PlayerId = "p2", Reponse = "x", EstCorrecte = true, TempsReponseMs = 500 },
                ],
            });
        session.SeriesList.Add(serie);

        var resultat = TitresService.CalculerTitres(session, ResolveurVide);

        var eclair = resultat.Single(t => t.Code == "ECLAIR");
        Assert.Equal(["p1"], eclair.PlayerIds);
    }

    [Fact]
    public void Eclair_SeuilNonAtteintNeDecerneRien()
    {
        var p1 = CreerJoueur("p1");
        var session = CreerSession(p1);
        var serie = NouvelleSerie(0);

        for (var i = 0; i < 2; i++)
            serie.Rounds.Add(new Round
            {
                TrackId = $"t{i}",
                Reponses = [new RoundAnswer { PlayerId = "p1", Reponse = "x", EstCorrecte = true, TempsReponseMs = 100 }],
            });
        session.SeriesList.Add(serie);

        var resultat = TitresService.CalculerTitres(session, ResolveurVide);

        Assert.DoesNotContain(resultat, t => t.Code == "ECLAIR");
    }

    [Fact]
    public void Eclair_EgaliteEstPartagee()
    {
        var p1 = CreerJoueur("p1");
        var p2 = CreerJoueur("p2");
        var session = CreerSession(p1, p2);
        var serie = NouvelleSerie(0);

        for (var i = 0; i < 3; i++)
            serie.Rounds.Add(new Round
            {
                TrackId = $"t{i}",
                Reponses =
                [
                    new RoundAnswer { PlayerId = "p1", Reponse = "x", EstCorrecte = true, TempsReponseMs = 100 },
                    new RoundAnswer { PlayerId = "p2", Reponse = "x", EstCorrecte = true, TempsReponseMs = 100 },
                ],
            });
        session.SeriesList.Add(serie);

        var resultat = TitresService.CalculerTitres(session, ResolveurVide);

        var eclair = resultat.Single(t => t.Code == "ECLAIR");
        Assert.Equal(["p1", "p2"], eclair.PlayerIds.OrderBy(id => id));
    }

    // ----- SNIPER -----

    [Fact]
    public void Sniper_AttribueAuMeilleurTauxParmiLesReponsesDonnees()
    {
        var p1 = CreerJoueur("p1");
        var p2 = CreerJoueur("p2");
        var session = CreerSession(p1, p2);
        var serie = NouvelleSerie(0);

        for (var i = 0; i < 4; i++)
        {
            var reponses = new List<RoundAnswer> { new() { PlayerId = "p1", Reponse = "x", EstCorrecte = true } };
            if (i < 2) reponses.Add(new RoundAnswer { PlayerId = "p2", Reponse = "x", EstCorrecte = i == 0 });
            serie.Rounds.Add(new Round { TrackId = $"t{i}", Reponses = reponses });
        }
        session.SeriesList.Add(serie);

        var resultat = TitresService.CalculerTitres(session, ResolveurVide);

        var sniper = resultat.Single(t => t.Code == "SNIPER");
        Assert.Equal(["p1"], sniper.PlayerIds);
    }

    // ----- SPECIALISTE -----

    [Fact]
    public void Specialiste_AttribueSurLeTagOuLeJoueurBrilleLePlus()
    {
        var p1 = CreerJoueur("p1");
        var p2 = CreerJoueur("p2");
        var session = CreerSession(p1, p2);
        var serie = NouvelleSerie(0);

        var tracks = new Dictionary<string, Track>
        {
            ["rock1"] = new() { Id = "rock1", Title = "a", Artist = "a", FilePath = "a", Tags = ["rock"] },
            ["rock2"] = new() { Id = "rock2", Title = "b", Artist = "b", FilePath = "b", Tags = ["rock"] },
            ["rock3"] = new() { Id = "rock3", Title = "c", Artist = "c", FilePath = "c", Tags = ["rock"] },
        };

        // p1 répond lentement (200 ms) uniquement aux 3 rounds "rock" : sous les 50 % de
        // participation (3/7), il n'est donc jamais candidat SNIPER ; sa présence sur seulement 3
        // corrects (pile le seuil ECLAIR) est battue par p2, bien plus rapide, pour ne pas lui
        // faire consommer son plafond de 2 titres avant d'atteindre SPECIALISTE.
        foreach (var trackId in tracks.Keys)
            serie.Rounds.Add(new Round
            {
                TrackId = trackId,
                Reponses = [new RoundAnswer { PlayerId = "p1", Reponse = "x", EstCorrecte = true, TempsReponseMs = 200 }],
            });

        // 4 rounds hors tag "rock" (3/7 ≈ 43 % < 80 %) : p2 y répond vite pour rafler ECLAIR à sa
        // place, sans jamais toucher au tag "rock" (donc jamais candidat SPECIALISTE).
        for (var i = 0; i < 3; i++)
            serie.Rounds.Add(new Round
            {
                TrackId = $"autre{i}",
                Reponses = [new RoundAnswer { PlayerId = "p2", Reponse = "x", EstCorrecte = true, TempsReponseMs = 50 }],
            });
        serie.Rounds.Add(new Round { TrackId = "autre3", Reponses = [] });
        session.SeriesList.Add(serie);

        var resultat = TitresService.CalculerTitres(session, id => tracks.GetValueOrDefault(id));

        var specialiste = resultat.Single(t => t.Code == "SPECIALISTE");
        Assert.Equal(["p1"], specialiste.PlayerIds);
        Assert.Contains("Rock", specialiste.Libelle);
    }

    // ----- HORLOGE -----

    [Fact]
    public void Horloge_AttribueAuPlusPetitEcartMoyenEnCibleAnnee()
    {
        var p1 = CreerJoueur("p1");
        var p2 = CreerJoueur("p2");
        var session = CreerSession(p1, p2);
        var serie = NouvelleSerie(0);

        for (var i = 0; i < 2; i++)
            serie.Rounds.Add(new Round
            {
                TrackId = $"t{i}",
                Cible = RoundCible.Annee,
                Reponses =
                [
                    new RoundAnswer { PlayerId = "p1", Reponse = "1990", EstCorrecte = true, EcartAnnee = 0 },
                    new RoundAnswer { PlayerId = "p2", Reponse = "1985", EstCorrecte = false, EcartAnnee = 5 },
                ],
            });
        session.SeriesList.Add(serie);

        var resultat = TitresService.CalculerTitres(session, ResolveurVide);

        var horloge = resultat.Single(t => t.Code == "HORLOGE");
        Assert.Equal(["p1"], horloge.PlayerIds);
    }

    // ----- ROI_BONUS -----

    [Fact]
    public void RoiBonus_AttribueAuPlusDePointsNetsGagnesEnBonus()
    {
        var p1 = CreerJoueur("p1");
        var p2 = CreerJoueur("p2");
        var session = CreerSession(p1, p2);
        var serie = NouvelleSerie(0);
        serie.BonusRound = new BonusRound
        {
            TrackId = "t0",
            Reponses =
            [
                new BonusAnswer { PlayerId = "p1", Reponse = "x", EstCorrecte = true, Points = 50 },
                new BonusAnswer { PlayerId = "p2", Reponse = "x", EstCorrecte = false, Points = -20 },
            ],
        };
        session.SeriesList.Add(serie);

        var resultat = TitresService.CalculerTitres(session, ResolveurVide);

        var roiBonus = resultat.Single(t => t.Code == "ROI_BONUS");
        Assert.Equal(["p1"], roiBonus.PlayerIds);
    }

    // ----- KAMIKAZE -----

    [Fact]
    public void Kamikaze_AttribueALaPlusGrandePartDeMisesAuPalierMaximum()
    {
        var p1 = CreerJoueur("p1");
        var p2 = CreerJoueur("p2");
        var session = CreerSession(p1, p2);

        for (var i = 0; i < 2; i++)
        {
            var serie = NouvelleSerie(i);
            serie.BonusRound = new BonusRound
            {
                TrackId = $"t{i}",
                Mises = [new BonusStake { PlayerId = "p1", PalierIndex = 3 }, new BonusStake { PlayerId = "p2", PalierIndex = 0 }],
            };
            session.SeriesList.Add(serie);
        }

        var resultat = TitresService.CalculerTitres(session, ResolveurVide);

        var kamikaze = resultat.Single(t => t.Code == "KAMIKAZE");
        Assert.Equal(["p1"], kamikaze.PlayerIds);
    }

    // ----- PRUDENT -----

    [Fact]
    public void Prudent_AttribueAuPlusDeMisesSafeOuAbstentions()
    {
        var p1 = CreerJoueur("p1");
        var p2 = CreerJoueur("p2");
        var session = CreerSession(p1, p2);

        for (var i = 0; i < 3; i++)
        {
            var serie = NouvelleSerie(i);
            serie.BonusRound = new BonusRound
            {
                TrackId = $"t{i}",
                Mises = [new BonusStake { PlayerId = "p1", PalierIndex = 0 }, new BonusStake { PlayerId = "p2", PalierIndex = 2 }],
            };
            session.SeriesList.Add(serie);
        }

        var resultat = TitresService.CalculerTitres(session, ResolveurVide);

        var prudent = resultat.Single(t => t.Code == "PRUDENT");
        Assert.Equal(["p1"], prudent.PlayerIds);
    }

    // ----- REMONTADA -----

    [Fact]
    public void Remontada_AttribueALaPlusForteRemonteeEntreLaMiPartieEtLaFin()
    {
        var p1 = CreerJoueur("p1", score: 300);
        var p2 = CreerJoueur("p2", score: 50);
        var p3 = CreerJoueur("p3", score: 10);
        var session = CreerSession(p1, p2, p3);
        var serie = NouvelleSerie(0);

        // Une seule unité comptée à mi-partie (2 rounds -> plafond ceil(2/2)=1) : ce round fixe le
        // classement de mi-partie, très différent du classement final (Player.Score ci-dessus).
        serie.Rounds.Add(new Round
        {
            TrackId = "t0",
            Reponses =
            [
                new RoundAnswer { PlayerId = "p1", Reponse = "x", EstCorrecte = false, Points = 10 },
                new RoundAnswer { PlayerId = "p2", Reponse = "x", EstCorrecte = false, Points = 50 },
                new RoundAnswer { PlayerId = "p3", Reponse = "x", EstCorrecte = false, Points = 100 },
            ],
        });
        serie.Rounds.Add(new Round { TrackId = "t1", Reponses = [] });
        session.SeriesList.Add(serie);

        var resultat = TitresService.CalculerTitres(session, ResolveurVide);

        var remontada = resultat.Single(t => t.Code == "REMONTADA");
        Assert.Equal(["p1"], remontada.PlayerIds);
    }

    // ----- PANNEAU -----

    [Fact]
    public void Panneau_AttribueAuPlusDeReponsesSurUnPiegeOuUneFeinte()
    {
        var p1 = CreerJoueur("p1");
        var p2 = CreerJoueur("p2");
        var session = CreerSession(p1, p2);
        var serie = NouvelleSerie(0);

        for (var i = 0; i < 2; i++)
            serie.Rounds.Add(new Round
            {
                TrackId = $"t{i}",
                Reponses =
                [
                    new RoundAnswer { PlayerId = "p1", Reponse = "x", EstCorrecte = false, OptionChoisieEstPiege = true },
                    new RoundAnswer { PlayerId = "p2", Reponse = "x", EstCorrecte = true },
                ],
            });
        session.SeriesList.Add(serie);

        var resultat = TitresService.CalculerTitres(session, ResolveurVide);

        var panneau = resultat.Single(t => t.Code == "PANNEAU");
        Assert.Equal(["p1"], panneau.PlayerIds);
    }

    // ----- JOKER_GACHE -----

    [Fact]
    public void JokerGache_AttribueAuPlusTotDansLaPartie()
    {
        var p1 = CreerJoueur("p1");
        var p2 = CreerJoueur("p2");
        var session = CreerSession(p1, p2);
        var serie = NouvelleSerie(0);

        serie.Rounds.Add(new Round { TrackId = "t0", Reponses = [] });
        serie.Rounds.Add(new Round
        {
            TrackId = "t1",
            Reponses = [new RoundAnswer { PlayerId = "p1", Reponse = "x", EstCorrecte = false, AvecJoker = true }],
        });
        serie.Rounds.Add(new Round
        {
            TrackId = "t2",
            Reponses = [new RoundAnswer { PlayerId = "p2", Reponse = "x", EstCorrecte = false, AvecJoker = true }],
        });
        session.SeriesList.Add(serie);

        var resultat = TitresService.CalculerTitres(session, ResolveurVide);

        var jokerGache = resultat.Single(t => t.Code == "JOKER_GACHE");
        Assert.Equal(["p1"], jokerGache.PlayerIds);
    }

    [Fact]
    public void JokerGache_JokerUtiliseMaisReponseCorrecte_NeCompePas()
    {
        var p1 = CreerJoueur("p1");
        var session = CreerSession(p1);
        var serie = NouvelleSerie(0);
        serie.Rounds.Add(new Round
        {
            TrackId = "t0",
            Reponses = [new RoundAnswer { PlayerId = "p1", Reponse = "x", EstCorrecte = true, AvecJoker = true }],
        });
        session.SeriesList.Add(serie);

        var resultat = TitresService.CalculerTitres(session, ResolveurVide);

        Assert.DoesNotContain(resultat, t => t.Code == "JOKER_GACHE");
    }

    [Fact]
    public void JokerGache_PersonneNAUtiliseSonJoker_NeDecerneRien()
    {
        var p1 = CreerJoueur("p1");
        var session = CreerSession(p1);
        var serie = NouvelleSerie(0);
        serie.Rounds.Add(new Round
        {
            TrackId = "t0",
            Reponses = [new RoundAnswer { PlayerId = "p1", Reponse = "x", EstCorrecte = false }],
        });
        session.SeriesList.Add(serie);

        var resultat = TitresService.CalculerTitres(session, ResolveurVide);

        Assert.DoesNotContain(resultat, t => t.Code == "JOKER_GACHE");
    }

    // ----- CHAT_NOIR -----

    [Fact]
    public void ChatNoir_AttribueAuPlusDePointsPerdusEnMauvaisesReponses()
    {
        var p1 = CreerJoueur("p1");
        var p2 = CreerJoueur("p2");
        var session = CreerSession(p1, p2);
        var serie = NouvelleSerie(0);

        serie.Rounds.Add(new Round
        {
            TrackId = "t0",
            Reponses =
            [
                new RoundAnswer { PlayerId = "p1", Reponse = "x", EstCorrecte = false, Points = -50 },
                new RoundAnswer { PlayerId = "p2", Reponse = "x", EstCorrecte = false, Points = -10 },
            ],
        });
        serie.Rounds.Add(new Round
        {
            TrackId = "t1",
            Reponses = [new RoundAnswer { PlayerId = "p1", Reponse = "x", EstCorrecte = false, Points = -50 }],
        });
        session.SeriesList.Add(serie);

        var resultat = TitresService.CalculerTitres(session, ResolveurVide);

        var chatNoir = resultat.Single(t => t.Code == "CHAT_NOIR");
        Assert.Equal(["p1"], chatNoir.PlayerIds);
    }

    // ----- FIDELE -----

    [Fact]
    public void Fidele_AttribueAQuiNaAucunAutreTitre()
    {
        var p1 = CreerJoueur("p1");
        var p2 = CreerJoueur("p2");
        var session = CreerSession(p1, p2);
        session.SeriesList.Add(NouvelleSerie(0)); // aucun round joué, personne ne remplit un seuil

        var resultat = TitresService.CalculerTitres(session, ResolveurVide);

        var fidele = resultat.Single(t => t.Code == "FIDELE");
        Assert.Equal(["p1", "p2"], fidele.PlayerIds.OrderBy(id => id));
    }

    // ----- Règles d'attribution -----

    [Fact]
    public void Attribution_AucunJoueurNaPlusDeDeuxTitresEtLeTitreExcedentaireEstAbandonne()
    {
        var p1 = CreerJoueur("p1");
        var p2 = CreerJoueur("p2");
        var session = CreerSession(p1, p2);
        var serie = NouvelleSerie(0);

        // p1 domine ECLAIR (idx 0), SNIPER (idx 1) et CHAT_NOIR (idx 9) — chacun avec p1 comme
        // unique gagnant (rareté 1 chacun) ; l'ordre du catalogue les départage à égalité de rareté.
        for (var i = 0; i < 3; i++)
            serie.Rounds.Add(new Round
            {
                TrackId = $"ok{i}",
                Reponses = [new RoundAnswer { PlayerId = "p1", Reponse = "x", EstCorrecte = true, TempsReponseMs = 100 }],
            });
        for (var i = 0; i < 2; i++)
            serie.Rounds.Add(new Round
            {
                TrackId = $"ko{i}",
                Reponses = [new RoundAnswer { PlayerId = "p1", Reponse = "x", EstCorrecte = false, Points = -20 }],
            });
        session.SeriesList.Add(serie);

        var resultat = TitresService.CalculerTitres(session, ResolveurVide);

        Assert.Equal(2, resultat.Count(t => t.PlayerIds.Contains("p1")));
        Assert.Contains(resultat, t => t.Code == "ECLAIR" && t.PlayerIds.Contains("p1"));
        Assert.Contains(resultat, t => t.Code == "SNIPER" && t.PlayerIds.Contains("p1"));
        // CHAT_NOIR n'a que p1 comme candidat : plafonné, il disparaît plutôt que d'être décerné à personne.
        Assert.DoesNotContain(resultat, t => t.Code == "CHAT_NOIR");
        Assert.Contains(resultat, t => t.Code == "FIDELE" && t.PlayerIds.Contains("p2"));
    }

    [Fact]
    public void Attribution_ChaqueJoueurEnAAuMoinsUnTitre()
    {
        var p1 = CreerJoueur("p1");
        var p2 = CreerJoueur("p2");
        var session = CreerSession(p1, p2);
        var serie = NouvelleSerie(0);
        serie.Rounds.Add(new Round
        {
            TrackId = "t0",
            Reponses = [new RoundAnswer { PlayerId = "p1", Reponse = "x", EstCorrecte = true, TempsReponseMs = 100 }],
        });
        session.SeriesList.Add(serie);

        var resultat = TitresService.CalculerTitres(session, ResolveurVide);

        foreach (var joueur in session.Players)
            Assert.Contains(resultat, t => t.PlayerIds.Contains(joueur.PlayerId));
    }
}
