using Blindify.Domain.Statistics;
using Blindify.Infrastructure.Configuration;
using Blindify.Infrastructure.Stats;
using Microsoft.Extensions.Options;

namespace Blindify.Tests.Stats;

public class StatsRepositoryTests : IDisposable
{
    private readonly string _statsPath = Path.Combine(Path.GetTempPath(), $"blindify-stats-{Guid.NewGuid()}.json");

    public void Dispose()
    {
        if (File.Exists(_statsPath)) File.Delete(_statsPath);
    }

    private IStatsRepository CreateRepository()
    {
        var options = Options.Create(new DataPathsOptions { TracksPath = "unused", StatsPath = _statsPath, FlagsPath = "unused", FlagsResolutionsPath = "unused", RootPath = "unused" });
        return new StatsRepository(options);
    }

    [Fact]
    public void GetPlayCount_FichierInexistant_RetourneZero()
    {
        var repo = CreateRepository();

        Assert.Equal(0, repo.GetPlayCount("a1b2c3"));
    }

    [Fact]
    public void IncrementPlayCount_PremierAppel_PasseAUn()
    {
        var repo = CreateRepository();

        repo.IncrementPlayCount("a1b2c3");

        Assert.Equal(1, repo.GetPlayCount("a1b2c3"));
    }

    [Fact]
    public void IncrementPlayCount_AppelsMultiples_Accumule()
    {
        var repo = CreateRepository();

        repo.IncrementPlayCount("a1b2c3");
        repo.IncrementPlayCount("a1b2c3");
        repo.IncrementPlayCount("a1b2c3");

        Assert.Equal(3, repo.GetPlayCount("a1b2c3"));
    }

    [Fact]
    public void IncrementPlayCount_PersisteSurDisque()
    {
        var repo = CreateRepository();
        repo.IncrementPlayCount("a1b2c3");

        var nouvelleInstance = CreateRepository();

        Assert.Equal(1, nouvelleInstance.GetPlayCount("a1b2c3"));
    }

    [Fact]
    public void IncrementPlayCount_NeTouchePasAuxAutresMorceaux()
    {
        var repo = CreateRepository();

        repo.IncrementPlayCount("a1b2c3");
        repo.IncrementPlayCount("autreId");
        repo.IncrementPlayCount("autreId");

        Assert.Equal(1, repo.GetPlayCount("a1b2c3"));
        Assert.Equal(2, repo.GetPlayCount("autreId"));
    }

    // ----- V2 (socle statistiques) -----

    [Fact]
    public void GetPlayCount_FichierV1SansReponsesNiConfusions_SeLitSansErreur()
    {
        // Entrée "v1" (avant l'ajout de Reponses/Confusions) — doit rester lisible telle quelle.
        File.WriteAllText(_statsPath, """{"a1b2c3": {"PlayCount": 3}}""");

        var repo = CreateRepository();

        Assert.Equal(3, repo.GetPlayCount("a1b2c3"));
    }

    [Fact]
    public void EnregistrerResultatsRound_FichierV1Existant_EnrichitSansPerdreLePlayCount()
    {
        File.WriteAllText(_statsPath, """{"a1b2c3": {"PlayCount": 3}}""");
        var repo = CreateRepository();

        repo.EnregistrerResultatsRound(new RoundStatsUpdate { TrackId = "a1b2c3", CleModeCible = "Qcm:Titre", N = 2, Correct = 1, Absent = 1, TempsCorrectCumulMs = 1500 });

        Assert.Equal(3, repo.GetPlayCount("a1b2c3")); // toujours là, jamais écrasé

        var nouvelleInstance = CreateRepository();
        Assert.Equal(3, nouvelleInstance.GetPlayCount("a1b2c3"));
    }

    [Fact]
    public void EnregistrerResultatsRound_AppelsMultiples_AccumuleLesCompteurs()
    {
        var repo = CreateRepository();

        repo.EnregistrerResultatsRound(new RoundStatsUpdate { TrackId = "a1b2c3", CleModeCible = "Qcm:Titre", N = 2, Correct = 1, Absent = 1, TempsCorrectCumulMs = 1000 });
        repo.EnregistrerResultatsRound(new RoundStatsUpdate { TrackId = "a1b2c3", CleModeCible = "Qcm:Titre", N = 3, Correct = 2, Absent = 0, TempsCorrectCumulMs = 2000 });

        var nouvelleInstance = CreateRepository();
        var persistedJson = File.ReadAllText(_statsPath);

        Assert.Contains("\"N\": 5", persistedJson);
        Assert.Contains("\"Correct\": 3", persistedJson);
        Assert.Contains("\"Absent\": 1", persistedJson);
        Assert.Contains("\"TempsCorrectCumulMs\": 3000", persistedJson);
    }

    [Fact]
    public void EnregistrerResultatsRound_AvecConfusions_AccumuleParTrackIdDeDistracteur()
    {
        var repo = CreateRepository();
        var update = new RoundStatsUpdate
        {
            TrackId = "a1b2c3",
            CleModeCible = "Qcm:Titre",
            N = 3,
            Confusions = [new ConfusionDelta { TrackId = "distracteur1", Presente = 3, Choisi = 2 }],
        };

        repo.EnregistrerResultatsRound(update);
        repo.EnregistrerResultatsRound(update);

        var persistedJson = File.ReadAllText(_statsPath);
        Assert.Contains("\"Presente\": 6", persistedJson);
        Assert.Contains("\"Choisi\": 4", persistedJson);
    }

    [Fact]
    public void EnregistrerResultatsRound_CleModeCibleDifferentes_RestentSeparees()
    {
        var repo = CreateRepository();

        repo.EnregistrerResultatsRound(new RoundStatsUpdate { TrackId = "a1b2c3", CleModeCible = "Qcm:Titre", N = 1, Correct = 1 });
        repo.EnregistrerResultatsRound(new RoundStatsUpdate { TrackId = "a1b2c3", CleModeCible = "Bonus:TapeReponse:Auteur", N = 1, Correct = 0 });

        var persistedJson = File.ReadAllText(_statsPath);
        Assert.Contains("Qcm:Titre", persistedJson);
        Assert.Contains("Bonus:TapeReponse:Auteur", persistedJson);
    }
}
