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
        var options = Options.Create(new DataPathsOptions { TracksPath = "unused", StatsPath = _statsPath });
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
}
