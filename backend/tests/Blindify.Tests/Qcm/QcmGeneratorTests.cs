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
    public void GenererOptions_EviteUnDistracteurDuMemeAuteurQuandDautresSontDisponibles()
    {
        var correct = new Track { Id = "a", Title = "Viva La Vida", Artist = "Coldplay", FilePath = "audio/a.mp3", Genres = ["pop"] };
        var pool = new List<Track>
        {
            correct,
            new() { Id = "b", Title = "Paradise", Artist = "Coldplay", FilePath = "audio/b.mp3", Genres = ["pop"] },
            NouveauTrack("c", genres: ["pop"]),
            NouveauTrack("d", genres: ["pop"]),
            NouveauTrack("e", genres: ["pop"])
        };
        var config = new GameConfig { ProbabiliteQcmPiege = 0 };

        var result = _generator.GenererOptions(correct, pool, config, new Random(3));

        Assert.DoesNotContain("b", result.OptionsTrackIds);
    }

    [Fact]
    public void GenererOptions_CatalogueRestreint_IncludeMemeAuteurPlutotQueBloquerLeRound()
    {
        var correct = new Track { Id = "a", Title = "Viva La Vida", Artist = "Coldplay", FilePath = "audio/a.mp3" };
        var pool = new List<Track>
        {
            correct,
            new() { Id = "b", Title = "Paradise", Artist = "Coldplay", FilePath = "audio/b.mp3" },
            NouveauTrack("c"),
            NouveauTrack("d")
        };
        var config = new GameConfig { ProbabiliteQcmPiege = 0 };

        var result = _generator.GenererOptions(correct, pool, config, new Random(5));

        Assert.Equal(4, result.OptionsTrackIds.Count);
        Assert.Contains("b", result.OptionsTrackIds);
    }

    [Fact]
    public void GenererOptions_EviteDeuxDistracteursDuMemeAuteurEntreEux()
    {
        // Retour utilisateur : un QCM avec deux mauvaises réponses créditées au même artiste est
        // illisible en cible Auteur (texte identique sur deux options). Ici "Dua Lipa" a deux
        // morceaux dans le pool pop, il ne doit en apparaître qu'un seul au maximum.
        var correct = new Track { Id = "a", Title = "Sous le vent", Artist = "Garou", FilePath = "audio/a.mp3", Genres = ["pop"] };
        var pool = new List<Track>
        {
            correct,
            new() { Id = "b", Title = "Levitating", Artist = "Dua Lipa", FilePath = "audio/b.mp3", Genres = ["pop"] },
            new() { Id = "c", Title = "Physical", Artist = "Dua Lipa", FilePath = "audio/c.mp3", Genres = ["pop"] },
            NouveauTrack("d", genres: ["pop"]),
            NouveauTrack("e", genres: ["pop"])
        };
        var config = new GameConfig { ProbabiliteQcmPiege = 0 };

        var result = _generator.GenererOptions(correct, pool, config, new Random(11));

        var artistesDistracteurs = result.OptionsTrackIds
            .Where(id => id != correct.Id)
            .Select(id => pool.First(t => t.Id == id).Artist);
        Assert.Equal(artistesDistracteurs.Distinct().Count(), artistesDistracteurs.Count());
    }

    [Fact]
    public void GenererOptions_PoolGenreTagInsuffisant_PrivilegieLesAnneesLesPlusProches()
    {
        var correct = new Track { Id = "a", Title = "Titre a", Artist = "Artiste a", FilePath = "audio/a.mp3", Genres = ["niche"], Year = 2000 };
        var pool = new List<Track>
        {
            correct,
            new() { Id = "loin1", Title = "T", Artist = "Artiste loin1", FilePath = "audio/loin1.mp3", Year = 1970 },
            new() { Id = "proche1", Title = "T", Artist = "Artiste proche1", FilePath = "audio/proche1.mp3", Year = 1999 },
            new() { Id = "proche2", Title = "T", Artist = "Artiste proche2", FilePath = "audio/proche2.mp3", Year = 2001 },
            new() { Id = "moyen", Title = "T", Artist = "Artiste moyen", FilePath = "audio/moyen.mp3", Year = 1985 },
            new() { Id = "loin2", Title = "T", Artist = "Artiste loin2", FilePath = "audio/loin2.mp3", Year = 2020 },
        };
        var config = new GameConfig { ProbabiliteQcmPiege = 0 };

        var result = _generator.GenererOptions(correct, pool, config, new Random(21));

        var distracteurs = result.OptionsTrackIds.Where(id => id != correct.Id).ToHashSet();
        Assert.Equal(["moyen", "proche1", "proche2"], distracteurs.OrderBy(x => x));
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

    [Fact]
    public void GenererOptions_PiegeMemeAuteurQueCorrect_NestPasUtiliseCommeDistracteur()
    {
        // Retour utilisateur : "Calvin Harris" présent deux fois en QCM (dont une fois comme
        // bonne réponse) — le piège curé pointait vers un morceau du MÊME artiste que la bonne
        // réponse. Le filtre par artiste s'appliquait déjà à tout le reste du pipeline, mais pas
        // à l'injection du piège. Ici le piège partage l'auteur de "correct" : il doit être
        // ignoré (repli sur les autres candidats), jamais forcé au prix d'un doublon de texte.
        var correct = new Track
        {
            Id = "a", Title = "One Kiss", Artist = "Calvin Harris", FilePath = "audio/a.mp3",
            Genres = ["edm"], TrapWith = ["piege"]
        };
        var pool = new List<Track>
        {
            correct,
            new() { Id = "piege", Title = "Summer", Artist = "Calvin Harris", FilePath = "audio/piege.mp3", Genres = ["edm"] },
            NouveauTrack("b", genres: ["edm"]),
            NouveauTrack("c", genres: ["edm"]),
            NouveauTrack("d", genres: ["edm"])
        };
        var config = new GameConfig { ProbabiliteQcmPiege = 1.0 };

        var result = _generator.GenererOptions(correct, pool, config, new Random(13));

        Assert.DoesNotContain("piege", result.OptionsTrackIds);
    }

    [Fact]
    public void GenererOptions_GenrePartageTropGenerique_NeSuffitPasSeulCommeDistracteur()
    {
        // Retour utilisateur : un featuring Dua Lipa/Angèle tagué "chanson"/"variété française"
        // (genres hérités de l'artiste·e invité·e) matchait avec Renaud comme distracteur, alors
        // que les deux n'ont musicalement rien en commun — le seul genre partagé ("chanson") est
        // en réalité l'un des plus fréquents du catalogue, un très mauvais critère de cohérence à
        // lui seul. Ici "chanson" est partagé par une grosse part du pool (générique, > 8 %) alors
        // que "pop" est rare (< 8 %) : seuls les distracteurs partageant "pop" doivent être retenus
        // par ce palier, "chanson" seul ne doit qualifier personne.
        var correct = new Track { Id = "a", Title = "T", Artist = "Correct", FilePath = "audio/a.mp3", Genres = ["chanson", "pop"] };

        // "correct" porte lui-même le genre "pop" (en plus de "chanson") : il compte donc aussi
        // dans la fréquence de "pop" sur le catalogue — d'où le +1 par rapport aux 3 candidats.
        var pool = new List<Track> { correct };
        pool.AddRange(Enumerable.Range(0, 3).Select(i => NouveauTrack($"pop{i}", genres: ["pop"])));
        pool.AddRange(Enumerable.Range(0, 9).Select(i => NouveauTrack($"chanson{i}", genres: ["chanson"])));
        pool.AddRange(Enumerable.Range(0, 40).Select(i => NouveauTrack($"filler{i}")));

        var config = new GameConfig { ProbabiliteQcmPiege = 0 };

        var result = _generator.GenererOptions(correct, pool, config, new Random(17));

        var distracteurs = result.OptionsTrackIds.Where(id => id != correct.Id).ToHashSet();
        Assert.Equal(["pop0", "pop1", "pop2"], distracteurs.OrderBy(x => x));
    }
}
