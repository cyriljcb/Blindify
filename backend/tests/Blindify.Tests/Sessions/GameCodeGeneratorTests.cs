using Blindify.Application.Sessions;

namespace Blindify.Tests.Sessions;

public class GameCodeGeneratorTests
{
    private readonly GameCodeGenerator _generator = new();

    [Fact]
    public void GenererCode_ProduitCinqCaracteresSansAmbiguite()
    {
        var code = _generator.GenererCode();

        Assert.Equal(5, code.Length);
        Assert.DoesNotContain('0', code);
        Assert.DoesNotContain('O', code);
        Assert.DoesNotContain('1', code);
        Assert.DoesNotContain('I', code);
    }

    [Fact]
    public void GenererCode_ProduitDesCodesDifferents()
    {
        var codes = Enumerable.Range(0, 20).Select(_ => _generator.GenererCode()).Distinct().ToList();

        Assert.True(codes.Count > 1);
    }
}
