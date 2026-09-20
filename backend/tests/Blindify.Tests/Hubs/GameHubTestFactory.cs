using Blindify.Api.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Blindify.Tests.Hubs;

/// <summary>Héberge Blindify.Api en mémoire, avec tracks.json/stats.json pointés vers des fichiers temporaires.</summary>
public class GameHubTestFactory : WebApplicationFactory<Program>
{
    public readonly string TracksPath = Path.Combine(Path.GetTempPath(), $"blindify-it-tracks-{Guid.NewGuid()}.json");
    public readonly string StatsPath = Path.Combine(Path.GetTempPath(), $"blindify-it-stats-{Guid.NewGuid()}.json");
    public readonly string FlagsPath = Path.Combine(Path.GetTempPath(), $"blindify-it-flags-{Guid.NewGuid()}.json");
    public readonly string FlagsResolutionsPath = Path.Combine(Path.GetTempPath(), $"blindify-it-flags-resolutions-{Guid.NewGuid()}.json");
    public readonly string RootPath = Path.Combine(Path.GetTempPath(), $"blindify-it-data-{Guid.NewGuid()}");

    public GameHubTestFactory()
    {
        // t5 : seul morceau NON "disney" du jeu de test (les 4 autres forcent Cible.Film, jamais
        // Titre/Auteur — voir RoundService.ChoisirCible) — nécessaire pour tester le joker en cible
        // Titre/Auteur (ex. GameHubJokerIntegrationTests, pochette floutée). Tag "test-cover" unique
        // pour le sélectionner de façon déterministe via ThemesVivier plutôt que de dépendre du tirage
        // aléatoire du catalogue entier.
        File.WriteAllText(TracksPath, """
            [
              { "id": "t1", "title": "Under the Sea", "artist": "Samuel E. Wright", "filePath": "audio/t1.mp3", "genres": ["disney"], "tags": [], "trapTextArtist": "Faux Artiste Test" },
              { "id": "t2", "title": "Circle of Life", "artist": "Elton John", "filePath": "audio/t2.mp3", "genres": ["disney"], "tags": [] },
              { "id": "t3", "title": "Let It Go", "artist": "Idina Menzel", "filePath": "audio/t3.mp3", "genres": ["disney"], "tags": [] },
              { "id": "t4", "title": "Hakuna Matata", "artist": "Nathan Lane", "filePath": "audio/t4.mp3", "genres": ["disney"], "tags": [] },
              { "id": "t5", "title": "Bohemian Rhapsody", "artist": "Queen", "filePath": "audio/t5.mp3", "coverPath": "covers/t5.jpg", "genres": [], "tags": ["test-cover"] }
            ]
            """);

        Directory.CreateDirectory(Path.Combine(RootPath, "audio"));
        File.WriteAllBytes(Path.Combine(RootPath, "audio", "t1.mp3"), [0x00, 0x01, 0x02]);

        Directory.CreateDirectory(Path.Combine(RootPath, "covers"));
        // 200x200 plutôt qu'une taille minuscule : le flou gaussien du joker (rayon 40, voir Program.cs)
        // exige une image nettement plus grande que le rayon, comme une vraie pochette (jamais 4x4 en pratique).
        using (var image = new Image<Rgba32>(200, 200))
            image.SaveAsJpeg(Path.Combine(RootPath, "covers", "t5.jpg"));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Data:TracksPath"] = TracksPath,
                ["Data:StatsPath"] = StatsPath,
                ["Data:FlagsPath"] = FlagsPath,
                ["Data:FlagsResolutionsPath"] = FlagsResolutionsPath,
                ["Data:RootPath"] = RootPath,
                // Vide explicitement : sinon hérite de la vraie valeur de appsettings.Development.json
                // (environnement de test "Development" par défaut), qui pointe vers le vrai dossier
                // host/ du repo — les tests qui en ont besoin le réactivent eux-mêmes (voir StaticFilesTests).
                ["Host:StaticPath"] = "",
                // Activé pour tester GameHub.AuthenticateAdmin — sans conséquence sur les autres
                // tests, qui n'appellent jamais cette méthode.
                ["Admin:RemoteControlPassword"] = "test-admin-pw"
            });
        });
    }

    /// <summary>Connexion configurée avec le même protocole JSON (enums en string) que le serveur.
    /// code/playerId (V2) : posés en query string de l'URL du hub, exactement comme le fait le client
    /// Flutter dès que le code de partie est connu — permet à GameHub.OnConnectedAsync de rattacher
    /// automatiquement le joueur à sa reconnexion, voir GameHub.ReconnexionAutomatique_*Tests.</summary>
    public HubConnection CreateHubConnection(string? code = null, string? playerId = null)
    {
        var chemin = "/hubs/game";
        if (code is not null || playerId is not null)
        {
            var query = string.Join("&", new[]
            {
                code is not null ? $"code={Uri.EscapeDataString(code)}" : null,
                playerId is not null ? $"playerId={Uri.EscapeDataString(playerId)}" : null
            }.Where(p => p is not null));
            chemin += $"?{query}";
        }

        return new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, chemin), options =>
            {
                options.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .AddJsonProtocol(options =>
            {
                foreach (var converter in ContractJsonOptions.Instance.Converters)
                    options.PayloadSerializerOptions.Converters.Add(converter);
            })
            .Build();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(TracksPath)) File.Delete(TracksPath);
        if (File.Exists(StatsPath)) File.Delete(StatsPath);
        if (File.Exists(FlagsPath)) File.Delete(FlagsPath);
        if (File.Exists(FlagsResolutionsPath)) File.Delete(FlagsResolutionsPath);
        if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
    }
}
