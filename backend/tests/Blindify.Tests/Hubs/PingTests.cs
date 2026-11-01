using System.Net.Http.Json;
using Blindify.Api.Contracts;

namespace Blindify.Tests.Hubs;

public class PingTests : IClassFixture<GameHubTestFactory>
{
    private readonly GameHubTestFactory _factory;

    public PingTests(GameHubTestFactory factory)
    {
        _factory = factory;
    }

    // Contrat lu par le scan de sous-réseau de l'app (app/lib/services/decouverte_serveur.dart) :
    // le champ "service" et sa valeur ne doivent pas changer sans mettre l'app à jour.
    [Fact]
    public async Task Ping_RetourneIdentifiantBlindify()
    {
        using var client = _factory.CreateClient();

        var json = await client.GetFromJsonAsync<Dictionary<string, string>>("/api/ping");

        Assert.Equal("blindify", json!["service"]);
    }
}
