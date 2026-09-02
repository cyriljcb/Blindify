using System.Net;
using System.Net.Http.Json;
using Blindify.Api.Contracts;
using Microsoft.Extensions.Configuration;

namespace Blindify.Tests.Hubs;

public class AdminRestartTests : IClassFixture<GameHubTestFactory>
{
    private readonly GameHubTestFactory _factory;

    public AdminRestartTests(GameHubTestFactory factory)
    {
        _factory = factory;
    }

    // GameHubTestFactory ne configure pas Admin:RestartPassword — hérite de la valeur vide
    // d'appsettings.json (fonctionnalité désactivée par défaut, voir Program.cs).
    [Fact]
    public async Task Restart_SansMotDePasseConfigure_Retourne503()
    {
        using var client = _factory.CreateClient();

        var reponse = await client.PostAsJsonAsync("/api/admin/restart", new RestartRequestDto("peu importe"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, reponse.StatusCode);
    }

    [Fact]
    public async Task Restart_MauvaisMotDePasse_Retourne401()
    {
        await using var factoryAvecMotDePasse = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:RestartPassword"] = "bon-mot-de-passe" })));
        using var client = factoryAvecMotDePasse.CreateClient();

        var reponse = await client.PostAsJsonAsync("/api/admin/restart", new RestartRequestDto("mauvais-mot-de-passe"));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Restart_BonMotDePasse_Retourne200()
    {
        await using var factoryAvecMotDePasse = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:RestartPassword"] = "bon-mot-de-passe" })));
        using var client = factoryAvecMotDePasse.CreateClient();

        var reponse = await client.PostAsJsonAsync("/api/admin/restart", new RestartRequestDto("bon-mot-de-passe"));

        Assert.True(reponse.IsSuccessStatusCode);
    }
}
