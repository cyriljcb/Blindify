using Blindify.Infrastructure.Configuration;
using Blindify.Infrastructure.Tracks;
using Microsoft.Extensions.Options;

namespace Blindify.Tests.Tracks;

public class TracksRepositoryTests : IDisposable
{
    private readonly string _tracksPath = Path.Combine(Path.GetTempPath(), $"blindify-tracks-{Guid.NewGuid()}.json");

    public void Dispose()
    {
        if (File.Exists(_tracksPath)) File.Delete(_tracksPath);
    }

    private ITracksRepository CreateRepository()
    {
        var options = Options.Create(new DataPathsOptions { TracksPath = _tracksPath, StatsPath = "unused" });
        return new TracksRepository(options);
    }

    [Fact]
    public void Constructeur_FichierIntrouvable_LeveUneException()
    {
        var options = Options.Create(new DataPathsOptions { TracksPath = Path.Combine(Path.GetTempPath(), "inexistant.json"), StatsPath = "unused" });

        Assert.Throws<FileNotFoundException>(() => new TracksRepository(options));
    }

    [Fact]
    public void GetAll_ChargeEtMappeLesMorceauxDepuisLeJson()
    {
        File.WriteAllText(_tracksPath, """
            [
              {
                "id": "a1b2c3",
                "title": "Under the Sea",
                "artist": "Samuel E. Wright",
                "album": "The Little Mermaid",
                "spotifyId": "3n3Ppam7vgaVa1iaRUc9Lp",
                "youtubeId": "PT2_F-1esPk",
                "durationMs": 174000,
                "genres": ["disney", "soundtrack"],
                "tags": ["disney", "annees-90"],
                "trapWith": ["autreId"],
                "year": 1989,
                "filePath": "audio/a1b2c3.mp3",
                "coverPath": "covers/a1b2c3.jpg",
                "addedAt": "2026-08-06T10:00:00Z"
              }
            ]
            """);

        var repo = CreateRepository();
        var tracks = repo.GetAll();

        Assert.Single(tracks);
        var track = tracks[0];
        Assert.Equal("a1b2c3", track.Id);
        Assert.Equal("Under the Sea", track.Title);
        Assert.Equal("Samuel E. Wright", track.Artist);
        Assert.Equal(["disney", "soundtrack"], track.Genres);
        Assert.Equal(["autreId"], track.TrapWith);
        Assert.Equal(1989, track.Year);
    }

    [Fact]
    public void GetById_IdInconnu_RetourneNull()
    {
        File.WriteAllText(_tracksPath, "[]");
        var repo = CreateRepository();

        Assert.Null(repo.GetById("inexistant"));
    }
}
