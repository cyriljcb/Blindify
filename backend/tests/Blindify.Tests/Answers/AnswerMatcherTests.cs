using Blindify.Application.Answers;

namespace Blindify.Tests.Answers;

public class AnswerMatcherTests
{
    private readonly AnswerMatcher _matcher = new();

    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("chat", "chat", 0)]
    [InlineData("", "abc", 3)]
    public void DistanceLevenshtein_CasConnus(string a, string b, int distanceAttendue)
    {
        Assert.Equal(distanceAttendue, _matcher.DistanceLevenshtein(a, b));
    }

    [Fact]
    public void Normaliser_RetireAccentsCasseEtPonctuation()
    {
        Assert.Equal("elephant", _matcher.Normaliser("Éléphant !"));
    }

    [Fact]
    public void EstCorrecte_ReponseExacte_EstAcceptee()
    {
        Assert.True(_matcher.EstCorrecte("Under the Sea", "Under the Sea", toleranceRatio: 0.2));
    }

    [Fact]
    public void EstCorrecte_FauteDeFrappeDansLaTolerance_EstAcceptee()
    {
        // "Under the Sae" vs "Under the Sea" : 1 caractère transposé, distance = 2, seuil = floor(13*0.2) = 2
        Assert.True(_matcher.EstCorrecte("Under the Sae", "Under the Sea", toleranceRatio: 0.2));
    }

    [Fact]
    public void EstCorrecte_ReponseTropDifferente_EstRejetee()
    {
        Assert.False(_matcher.EstCorrecte("Autre Chose Completement", "Under the Sea", toleranceRatio: 0.2));
    }

    [Fact]
    public void EstCorrecte_TitreCourt_SeuilMinimalDUnCaractere()
    {
        // longueur 2, seuil = max(1, floor(2*0.2)) = 1
        Assert.True(_matcher.EstCorrecte("Xo", "Go", toleranceRatio: 0.2));
        Assert.False(_matcher.EstCorrecte("Xy", "Go", toleranceRatio: 0.2));
    }
}
