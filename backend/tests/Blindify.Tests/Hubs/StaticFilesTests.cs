using Microsoft.Extensions.Configuration;

namespace Blindify.Tests.Hubs;

public class StaticFilesTests : IClassFixture<GameHubTestFactory>
{
    private readonly GameHubTestFactory _factory;

    public StaticFilesTests(GameHubTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetFichierAudio_ExistantSousRootPath_Retourne200()
    {
        using var client = _factory.CreateClient();

        var reponse = await client.GetAsync("/files/audio/t1.mp3");

        Assert.True(reponse.IsSuccessStatusCode);
        var contenu = await reponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0x00, 0x01, 0x02 }, contenu);
    }

    [Fact]
    public async Task GetFichierAudio_Inexistant_Retourne404()
    {
        using var client = _factory.CreateClient();

        var reponse = await client.GetAsync("/files/audio/inexistant.mp3");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, reponse.StatusCode);
    }

    // Retour utilisateur (playtest 2026-08-24) : le backend peut désormais servir host/ (voir
    // Program.cs) — mais reste optionnel, sans effet quand Host:StaticPath n'est pas configuré
    // (GameHubTestFactory le vide explicitement, voir son commentaire).
    [Fact]
    public async Task GetRacine_SansHostStaticPathConfigure_Retourne404()
    {
        using var client = _factory.CreateClient();

        var reponse = await client.GetAsync("/");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, reponse.StatusCode);
    }

    [Fact]
    public async Task GetRacine_AvecHostStaticPathConfigure_SertIndexHtmlParDefaut()
    {
        var hostDir = Path.Combine(Path.GetTempPath(), $"blindify-it-host-{Guid.NewGuid()}");
        Directory.CreateDirectory(hostDir);
        await File.WriteAllTextAsync(Path.Combine(hostDir, "index.html"), "<html>panneau de controle</html>");
        await File.WriteAllTextAsync(Path.Combine(hostDir, "display.html"), "<html>ecran public</html>");

        try
        {
            await using var factoryAvecHost = _factory.WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?> { ["Host:StaticPath"] = hostDir })));

            using var client = factoryAvecHost.CreateClient();

            var reponseRacine = await client.GetAsync("/");
            Assert.True(reponseRacine.IsSuccessStatusCode);
            Assert.Contains("panneau de controle", await reponseRacine.Content.ReadAsStringAsync());

            var reponseDisplay = await client.GetAsync("/display.html");
            Assert.True(reponseDisplay.IsSuccessStatusCode);
            Assert.Contains("ecran public", await reponseDisplay.Content.ReadAsStringAsync());
        }
        finally
        {
            Directory.Delete(hostDir, recursive: true);
        }
    }
}
