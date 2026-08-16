using Blindify.Application.Qcm;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;

namespace Blindify.Tests.Qcm;

public class QcmGeneratorTests
{
    private readonly QcmGenerator _generator = new();

    private static Track NouveauTrack(string id, List<string>? genres = null, List<string>? tags = null, List<string>? trapWith = null) => new()
    {
        Id = id,
        Title = $"Titre {id}",
        Artist = $"Artiste {id}",
        FilePath = $"audio/{id}.mp3",
        Genres = genres ?? [],
        Tags = tags ?? [],
        TrapWith = trapWith ?? []
    };

    [Fact]
    public void GenererOptions_RetourneToujoursQuatreOptionsAvecLaBonneReponse()
    {
        var correct = NouveauTrack("a", genres: ["pop"]);
        var pool = new List<Track>
        {
            correct,
            NouveauTrack("b", genres: ["pop"]),
            NouveauTrack("c", genres: ["pop"]),
            NouveauTrack("d", genres: ["pop"]),
            NouveauTrack("e", genres: ["rock"])
        };
        var config = new GameConfig { ProbabiliteQcmPiege = 0 };

        var result = _generator.GenererOptions(correct, pool, config, new Random(42));

        Assert.Equal(4, result.OptionsTrackIds.Count);
        Assert.Equal(4, result.OptionsTrackIds.Distinct().Count());
        Assert.Contains(correct.Id, result.OptionsTrackIds);
        Assert.Equal(correct.Id, result.CorrectTrackId);
    }

    [Fact]
    public void GenererOptions_PoolGenreTagInsuffisant_CompleteAvecLePoolGlobal()
    {
        var correct = NouveauTrack("a", genres: ["niche"]);
        var pool = new List<Track>
        {
            correct,
            NouveauTrack("b", genres: ["pop"]),
            NouveauTrack("c", genres: ["rock"]),
            NouveauTrack("d", genres: ["jazz"])
        };
        var config = new GameConfig { ProbabiliteQcmPiege = 0 };

        var result = _generator.GenererOptions(correct, pool, config, new Random(1));

        Assert.Equal(4, result.OptionsTrackIds.Count);
        Assert.Equal(4, result.OptionsTrackIds.Distinct().Count());
    }

    [Fact]
    public void GenererOptions_ProbabiliteMaximale_UtiliseUnPiegeSiDisponible()
    {
        var correct = NouveauTrack("a", genres: ["pop"], trapWith: ["piege"]);
        var pool = new List<Track>
        {
            correct,
            NouveauTrack("piege", genres: ["pop"]),
            NouveauTrack("b", genres: ["pop"]),
            NouveauTrack("c", genres: ["pop"])
        };
        var config = new GameConfig { ProbabiliteQcmPiege = 1.0 };

        var result = _generator.GenererOptions(correct, pool, config, new Random(7));

        Assert.Contains("piege", result.OptionsTrackIds);
    }
}
