using Blindify.Application.Qcm;

namespace Blindify.Tests.Qcm;

public class AnneeQcmGeneratorTests
{
    private readonly AnneeQcmGenerator _generator = new();

    [Fact]
    public void GenererOptions_RetourneQuatreAnneesTrieesContenantLaBonneReponse()
    {
        var options = _generator.GenererOptions(1990, new Random(42));

        Assert.Equal(4, options.Count);
        Assert.Contains(1990, options);
        Assert.Equal(options.OrderBy(a => a), options);
    }

    [Fact]
    public void GenererOptions_EcartMinimalDeDeuxAnsEntreOptionsConsecutives()
    {
        for (var i = 0; i < 200; i++)
        {
            var options = _generator.GenererOptions(1990, new Random(i));
            for (var j = 1; j < options.Count; j++)
                Assert.True(options[j] - options[j - 1] >= 2, $"écart insuffisant entre {options[j - 1]} et {options[j]} (seed {i})");
        }
    }

    [Fact]
    public void GenererOptions_JamaisPlusDeDixAnsDeLaBonneReponse()
    {
        for (var i = 0; i < 200; i++)
        {
            var options = _generator.GenererOptions(1990, new Random(i));
            Assert.All(options, a => Assert.True(Math.Abs(a - 1990) <= 10, $"écart trop grand : {a} (seed {i})"));
        }
    }

    [Fact]
    public void GenererOptions_JamaisDansLeFutur()
    {
        var anneeMax = DateTime.UtcNow.Year;
        for (var i = 0; i < 200; i++)
        {
            var options = _generator.GenererOptions(anneeMax - 1, new Random(i));
            Assert.All(options, a => Assert.True(a <= anneeMax, $"année dans le futur : {a} (seed {i})"));
        }
    }

    [Fact]
    public void GenererOptions_SurBeaucoupDessais_LaPositionDeLaBonneReponseVarie()
    {
        var positions = new HashSet<int>();
        for (var i = 0; i < 200; i++)
        {
            var options = _generator.GenererOptions(1990, new Random(i));
            positions.Add(options.IndexOf(1990));
        }

        // Les 4 positions (0..3) doivent finir par toutes apparaître sur 200 tirages.
        Assert.Equal(4, positions.Count);
    }
}
