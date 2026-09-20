using Blindify.Application.Stats;
using Blindify.Domain.Entities;
using Blindify.Domain.Enums;

namespace Blindify.Tests.Stats;

public class RoundStatsAggregatorTests
{
    [Fact]
    public void PourRoundClassique_CompteCorrectsEtAbsentsDansN()
    {
        var round = new Round
        {
            TrackId = "a",
            Mode = RoundMode.TapeReponse,
            Cible = RoundCible.Titre,
            Reponses =
            [
                new RoundAnswer { PlayerId = "p1", Reponse = "x", EstCorrecte = true, TempsReponseMs = 1000 },
                new RoundAnswer { PlayerId = "p2", Reponse = "y", EstCorrecte = false, TempsReponseMs = 2000 },
                new RoundAnswer { PlayerId = "p3", Reponse = "", EstCorrecte = false, EstAbsent = true },
            ],
        };

        var update = RoundStatsAggregator.PourRoundClassique(round);

        Assert.Equal("a", update.TrackId);
        Assert.Equal("TapeReponse:Titre", update.CleModeCible);
        Assert.Equal(3, update.N);
        Assert.Equal(1, update.Correct);
        Assert.Equal(1, update.Absent);
        Assert.Equal(1000, update.TempsCorrectCumulMs);
    }

    [Fact]
    public void PourBonus_PrefixeLaCleAvecBonus()
    {
        var bonusRound = new BonusRound
        {
            TrackId = "a",
            Mode = RoundMode.Qcm,
            Cible = RoundCible.Auteur,
            Reponses = [new BonusAnswer { PlayerId = "p1", Reponse = "a", EstCorrecte = true, TempsReponseMs = 500 }],
        };

        var update = RoundStatsAggregator.PourBonus(bonusRound);

        Assert.Equal("Bonus:Qcm:Auteur", update.CleModeCible);
        Assert.Equal(1, update.N);
        Assert.Equal(1, update.Correct);
    }

    [Fact]
    public void PourRoundClassique_Confusions_ExcluLaBonneReponseEtLesOptionsFeintees()
    {
        var round = new Round
        {
            TrackId = "correct",
            Mode = RoundMode.Qcm,
            Cible = RoundCible.Titre,
            Options =
            [
                new RoundOption { TrackId = "correct", TexteAffiche = "Bonne réponse", EstFeinte = false, EstPiege = false },
                new RoundOption { TrackId = "distracteur1", TexteAffiche = "D1", EstFeinte = false, EstPiege = false },
                new RoundOption { TrackId = "distracteur2-feinte", TexteAffiche = "D2 (feinte)", EstFeinte = true, EstPiege = false },
                new RoundOption { TrackId = "distracteur3-piege", TexteAffiche = "D3", EstFeinte = false, EstPiege = true },
            ],
            Reponses =
            [
                new RoundAnswer { PlayerId = "p1", Reponse = "correct", EstCorrecte = true, OptionChoisieTrackId = "correct" },
                new RoundAnswer { PlayerId = "p2", Reponse = "distracteur1", EstCorrecte = false, OptionChoisieTrackId = "distracteur1" },
                new RoundAnswer { PlayerId = "p3", Reponse = "distracteur3-piege", EstCorrecte = false, OptionChoisieTrackId = "distracteur3-piege" },
            ],
        };

        var update = RoundStatsAggregator.PourRoundClassique(round);

        // 3 répondants -> chaque distracteur non feinté est "présenté" 3 fois.
        Assert.Equal(2, update.Confusions.Count); // distracteur1 + distracteur3-piege, jamais "correct" ni la feinte
        var d1 = update.Confusions.Single(c => c.TrackId == "distracteur1");
        Assert.Equal(3, d1.Presente);
        Assert.Equal(1, d1.Choisi);
        var d3 = update.Confusions.Single(c => c.TrackId == "distracteur3-piege");
        Assert.Equal(3, d3.Presente);
        Assert.Equal(1, d3.Choisi);
        Assert.DoesNotContain(update.Confusions, c => c.TrackId == "correct");
        Assert.DoesNotContain(update.Confusions, c => c.TrackId == "distracteur2-feinte");
    }

    [Fact]
    public void PourRoundClassique_CibleAnnee_CumuleLesEcarts()
    {
        var round = new Round
        {
            TrackId = "a",
            Mode = RoundMode.TapeReponse,
            Cible = RoundCible.Annee,
            Reponses =
            [
                new RoundAnswer { PlayerId = "p1", Reponse = "1990", EstCorrecte = true, EcartAnnee = 0 },
                new RoundAnswer { PlayerId = "p2", Reponse = "1985", EstCorrecte = false, EcartAnnee = 5 },
            ],
        };

        var update = RoundStatsAggregator.PourRoundClassique(round);

        Assert.Equal(5, update.EcartCumul);
    }

    [Fact]
    public void PourRoundClassique_CibleAutreQueAnnee_EcartCumulEstNull()
    {
        var round = new Round { TrackId = "a", Mode = RoundMode.TapeReponse, Cible = RoundCible.Titre };

        var update = RoundStatsAggregator.PourRoundClassique(round);

        Assert.Null(update.EcartCumul);
    }
}
