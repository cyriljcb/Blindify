using Blindify.Application.Rounds;

namespace Blindify.Tests.Rounds;

/// <summary>Vote des thèmes (retour utilisateur 2026-10-17) — voir SeriesPlanner.ChoisirThemesDepuisVotes.</summary>
public class SeriesPlannerVotesTests
{
    [Fact]
    public void AutantDeSeriesQueDeThemesVotes_TousLesThemesSontJoues()
    {
        var votes = new Dictionary<string, List<string>>
        {
            ["alice"] = ["rock", "pop"],
            ["bob"] = ["pop", "rap"],
        };

        var resultat = SeriesPlanner.ChoisirThemesDepuisVotes(votes, 3);

        Assert.Equal(["pop", "rap", "rock"], resultat.OrderBy(t => t));
    }

    [Fact]
    public void PlusDeSeriesQueDeThemesVotes_EstRefuse()
    {
        var votes = new Dictionary<string, List<string>> { ["alice"] = ["rock"], ["bob"] = ["rock"] };

        var ex = Assert.Throws<ArgumentException>(() => SeriesPlanner.ChoisirThemesDepuisVotes(votes, 2));
        Assert.Contains("1 thème", ex.Message);
    }

    [Fact]
    public void AucunVote_EstRefuse()
    {
        Assert.Throws<ArgumentException>(() => SeriesPlanner.ChoisirThemesDepuisVotes(new Dictionary<string, List<string>>(), 1));
    }

    [Fact]
    public void MoinsDeSeriesQueDeThemes_ChaqueVotantAAuMoinsUnThemeQuandCEstPossible()
    {
        // 3 votants, 3 séries, 7 thèmes votés : un thème par votant est toujours atteignable.
        var votes = new Dictionary<string, List<string>>
        {
            ["alice"] = ["rock", "metal"],
            ["bob"] = ["pop", "disco"],
            ["chloe"] = ["rap", "jazz", "funk"],
        };

        for (var graine = 0; graine < 200; graine++)
        {
            var resultat = SeriesPlanner.ChoisirThemesDepuisVotes(votes, 3, new Random(graine));

            Assert.Equal(3, resultat.Distinct().Count());
            Assert.All(votes.Values, v => Assert.Contains(resultat, v.Contains));
        }
    }

    [Fact]
    public void ThemePartage_CouvreDeuxVotantsAvecUneSeuleSerie()
    {
        // Avec une seule série, seul « pop » satisfait tout le monde.
        var votes = new Dictionary<string, List<string>>
        {
            ["alice"] = ["rock", "pop"],
            ["bob"] = ["pop", "rap"],
        };

        for (var graine = 0; graine < 50; graine++)
            Assert.Equal(["pop"], SeriesPlanner.ChoisirThemesDepuisVotes(votes, 1, new Random(graine)));
    }

    [Fact]
    public void PlusDeVotantsQueDeSeries_ResteAleatoireEntreLesThemesEquivalents()
    {
        var votes = new Dictionary<string, List<string>>
        {
            ["alice"] = ["rock"],
            ["bob"] = ["pop"],
            ["chloe"] = ["rap"],
        };

        var tires = Enumerable.Range(0, 100)
            .Select(g => SeriesPlanner.ChoisirThemesDepuisVotes(votes, 1, new Random(g)).Single())
            .ToHashSet();

        Assert.Equal(3, tires.Count);
    }
}
