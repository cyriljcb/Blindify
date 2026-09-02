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
        Assert.True(_matcher.EstCorrecte(
            "Under the Sea", "Under the Sea",
            toleranceRatio: 0.2, longueurMinimalePourTolerance: 10,
            longueurMinimalePourToleranceFixe: 4, toleranceFixeReponseCourte: 2));
    }

    [Fact]
    public void EstCorrecte_FauteDeFrappeDansLaTolerance_EstAcceptee()
    {
        // "Under the Sae" vs "Under the Sea" : 1 caractère transposé, distance = 2, seuil = floor(13*0.2) = 2
        Assert.True(_matcher.EstCorrecte(
            "Under the Sae", "Under the Sea",
            toleranceRatio: 0.2, longueurMinimalePourTolerance: 10,
            longueurMinimalePourToleranceFixe: 4, toleranceFixeReponseCourte: 2));
    }

    [Fact]
    public void EstCorrecte_ReponseTropDifferente_EstRejetee()
    {
        Assert.False(_matcher.EstCorrecte(
            "Autre Chose Completement", "Under the Sea",
            toleranceRatio: 0.2, longueurMinimalePourTolerance: 10,
            longueurMinimalePourToleranceFixe: 4, toleranceFixeReponseCourte: 2));
    }

    [Fact]
    public void EstCorrecte_TitreTresCourt_ExigeReponseExacte()
    {
        // longueur 2 < longueurMinimalePourToleranceFixe (4) : aucune tolérance, réponse exacte exigée
        Assert.True(_matcher.EstCorrecte(
            "Go", "Go",
            toleranceRatio: 0.2, longueurMinimalePourTolerance: 10,
            longueurMinimalePourToleranceFixe: 4, toleranceFixeReponseCourte: 2));
        Assert.False(_matcher.EstCorrecte(
            "Xo", "Go",
            toleranceRatio: 0.2, longueurMinimalePourTolerance: 10,
            longueurMinimalePourToleranceFixe: 4, toleranceFixeReponseCourte: 2));
        Assert.False(_matcher.EstCorrecte(
            "Xy", "Go",
            toleranceRatio: 0.2, longueurMinimalePourTolerance: 10,
            longueurMinimalePourToleranceFixe: 4, toleranceFixeReponseCourte: 2));
    }

    [Fact]
    public void EstCorrecte_TitreMoyen_ToleranceFixeAppliquee()
    {
        // Retour utilisateur : "Hayley" tapé pour "Halsey" (6 caractères, entre les deux seuils de
        // longueur) comptait à tort faux car la réponse exacte était exigée sous 10 caractères.
        // Distance("hayley", "halsey") = 2 = toleranceFixeReponseCourte : accepté.
        Assert.True(_matcher.EstCorrecte(
            "Hayley", "Halsey",
            toleranceRatio: 0.2, longueurMinimalePourTolerance: 10,
            longueurMinimalePourToleranceFixe: 4, toleranceFixeReponseCourte: 2));

        // Mais une réponse trop différente sur cette même longueur reste rejetée (distance 4 > 2).
        Assert.False(_matcher.EstCorrecte(
            "Zzzzzz", "Halsey",
            toleranceRatio: 0.2, longueurMinimalePourTolerance: 10,
            longueurMinimalePourToleranceFixe: 4, toleranceFixeReponseCourte: 2));
    }

    [Fact]
    public void EstCorrecte_TitreJusteAuDessusDuSeuil_ToleranceRatioReactivee()
    {
        // longueur 10 == longueurMinimalePourTolerance : le ratio (et non plus la tolérance fixe) s'applique
        // "abcdefghij" (10) vs "abcdefghik" : 1 caractère substitué, distance = 1, seuil = floor(10*0.2) = 2
        Assert.True(_matcher.EstCorrecte(
            "abcdefghik", "abcdefghij",
            toleranceRatio: 0.2, longueurMinimalePourTolerance: 10,
            longueurMinimalePourToleranceFixe: 4, toleranceFixeReponseCourte: 2));
    }
}
