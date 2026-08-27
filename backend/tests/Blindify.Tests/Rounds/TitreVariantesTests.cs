using Blindify.Application.Rounds;

namespace Blindify.Tests.Rounds;

public class TitreVariantesTests
{
    [Theory]
    [InlineData("Let It Go")] // 3 mots
    [InlineData("Under the Sea Now")] // 4 mots
    public void Acceptables_TitreCourt_SeulementLeTitreComplet(string titre)
    {
        Assert.Equal([titre], TitreVariantes.Acceptables(titre));
    }

    [Fact]
    public void Acceptables_TitreLong_ContientLaVarianteTronqueeEtLeTitreComplet()
    {
        // Retour utilisateur (playtest 2026-08-24) : "Another One Bites The Dust" (5 mots) doit
        // rester devinable même sans taper le titre en entier.
        const string titre = "Another One Bites The Dust";

        var acceptables = TitreVariantes.Acceptables(titre).ToList();

        Assert.Contains(titre, acceptables);
        Assert.Contains("Another One Bites", acceptables);
    }

    [Theory]
    [InlineData("Let It Go", true)] // 3 mots
    [InlineData("Another One Bites The Dust", true)] // 5 mots, tronqué mais toujours éligible
    [InlineData("Un Titre Avec Beaucoup Trop De Mots Dedans", false)] // 8 mots, au-delà du seuil d'exclusion
    public void EstEligibleCommeCible_SelonLeNombreDeMots(string titre, bool attendu)
    {
        Assert.Equal(attendu, TitreVariantes.EstEligibleCommeCible(titre));
    }
}
