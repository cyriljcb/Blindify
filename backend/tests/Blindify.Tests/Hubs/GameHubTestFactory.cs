using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Blindify.Tests.Hubs;

/// <summary>Héberge Blindify.Api en mémoire, avec tracks.json/stats.json pointés vers des fichiers temporaires.</summary>
public class GameHubTestFactory : WebApplicationFactory<Program>
{
    public readonly string TracksPath = Path.Combine(Path.GetTempPath(), $"blindify-it-tracks-{Guid.NewGuid()}.json");
    public readonly string StatsPath = Path.Combine(Path.GetTempPath(), $"blindify-it-stats-{Guid.NewGuid()}.json");
    public readonly string RootPath = Path.Combine(Path.GetTempPath(), $"blindify-it-data-{Guid.NewGuid()}");

    public GameHubTestFactory()
    {
        File.WriteAllText(TracksPath, """
            [
              { "id": "t1", "title": "Under the Sea", "artist": "Samuel E. Wright", "filePath": "audio/t1.mp3", "genres": ["disney"], "tags": [] },
              { "id": "t2", "title": "Circle of Life", "artist": "Elton John", "filePath": "audio/t2.mp3", "genres": ["disney"], "tags": [] },
              { "id": "t3", "title": "Let It Go", "artist": "Idina Menzel", "filePath": "audio/t3.mp3", "genres": ["disney"], "tags": [] },
              { "id": "t4", "title": "Hakuna Matata", "artist": "Nathan Lane", "filePath": "audio/t4.mp3", "genres": ["disney"], "tags": [] }
            ]
            """);

        Directory.CreateDirectory(Path.Combine(RootPath, "audio"));
        File.WriteAllBytes(Path.Combine(RootPath, "audio", "t1.mp3"), [0x00, 0x01, 0x02]);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Data:TracksPath"] = TracksPath,
                ["Data:StatsPath"] = StatsPath,
                ["Data:RootPath"] = RootPath
            });
        });
    }

    /// <summary>Connexion configurée avec le même protocole JSON (enums en string) que le serveur.</summary>
    public HubConnection CreateHubConnection() =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, "/hubs/game"), options =>
            {
                options.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(TracksPath)) File.Delete(TracksPath);
        if (File.Exists(StatsPath)) File.Delete(StatsPath);
        if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
    }
}
