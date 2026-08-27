using Blindify.Application.Rounds;

namespace Blindify.Tests.Rounds;

public class TitreVariantesTests
{
    [Theory]
    [InlineData("Let It Go")] // 9 caractères
    [InlineData("Under the Sea Now")] // 17 caractères
    public void Acceptables_TitreCourt_SeulementLeTitreComplet(string titre)
    {
        Assert.Equal([titre], TitreVariantes.Acceptables(titre));
    }

    [Fact]
    public void Acceptables_TitreLong_ContientLaVarianteTronqueeEtLeTitreComplet()
    {
        // Retour utilisateur (playtest 2026-08-24, raisonnement en caractères depuis le
        // 2026-08-27) : "Another One Bites The Dust" (26 caractères) doit rester devinable même
        // sans taper le titre en entier.
        const string titre = "Another One Bites The Dust";

        var acceptables = TitreVariantes.Acceptables(titre).ToList();

        Assert.Contains(titre, acceptables);
        Assert.Contains("Another One Bites", acceptables);
    }

    [Theory]
    [InlineData("Let It Go", true)] // 9 caractères
    [InlineData("Another One Bites The Dust", true)] // 26 caractères, tronqué mais toujours éligible
    [InlineData("Un Titre Avec Beaucoup Trop De Mots Dedans", false)] // 42 caractères, au-delà du seuil d'exclusion
    public void EstEligibleCommeCible_SelonLeNombreDeCaracteres(string titre, bool attendu)
    {
        Assert.Equal(attendu, TitreVariantes.EstEligibleCommeCible(titre));
    }
}
