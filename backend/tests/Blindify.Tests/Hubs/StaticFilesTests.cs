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
}
