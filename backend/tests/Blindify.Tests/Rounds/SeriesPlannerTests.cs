using Blindify.Application.Rounds;
using Blindify.Domain.Enums;

namespace Blindify.Tests.Rounds;

public class SeriesPlannerTests
{
    [Fact]
    public void AssignerThemesAuxSeries_ViverVide_RetourneUneListeVideParSerie()
    {
        var resultat = SeriesPlanner.AssignerThemesAuxSeries([], 3);

        Assert.Equal(3, resultat.Count);
        Assert.All(resultat, tags => Assert.Empty(tags));
    }

    [Fact]
    public void AssignerThemesAuxSeries_PasPlusDeSeriesQueDeThemes_AucuneRepetition()
    {
        var themes = new List<string> { "rock", "pop", "rap" };

        var resultat = SeriesPlanner.AssignerThemesAuxSeries(themes, 3);

        Assert.Equal(3, resultat.Count);
        Assert.All(resultat, tags => Assert.Single(tags));
        var themesObtenus = resultat.Select(tags => tags[0]).ToList();
        Assert.Equal(themes.OrderBy(t => t), themesObtenus.OrderBy(t => t));
    }

    [Fact]
    public void AssignerThemesAuxSeries_PlusDeSeriesQueDeThemes_RepiocheApresEpuisement()
    {
        var themes = new List<string> { "rock", "pop" };

        var resultat = SeriesPlanner.AssignerThemesAuxSeries(themes, 5);

        Assert.Equal(5, resultat.Count);
        Assert.All(resultat, tags => Assert.Single(tags));

        // Chaque bloc de 2 séries consécutives (taille du vivier) ne doit jamais répéter un thème
        // avant d'avoir repioché — regroupe les 5 tirages en blocs de 2 et vérifie l'absence de
        // doublon à l'intérieur de chaque bloc complet.
        var themesObtenus = resultat.Select(tags => tags[0]).ToList();
        for (var i = 0; i + 1 < themesObtenus.Count; i += 2)
            Assert.NotEqual(themesObtenus[i], themesObtenus[i + 1]);
    }

    [Fact]
    public void PaliersPourSerie_UneSeuleSerie_GardeLaBaseTelleQuelle()
    {
        var paliers = SeriesPlanner.PaliersPourSerie(0, 1);

        Assert.Equal(new[] { 10, 20, 30, 50 }, paliers);
    }

    [Fact]
    public void PaliersPourSerie_PremiereSerie_EstToujoursLaBase()
    {
        var paliers = SeriesPlanner.PaliersPourSerie(0, 10);

        Assert.Equal(new[] { 10, 20, 30, 50 }, paliers);
    }

    [Fact]
    public void PaliersPourSerie_DerniereSerie_AtteintExactementLePlafond()
    {
        // architecture.md section 7 : avec 10 séries, la dernière (index 9) doit atteindre
        // exactement [600, 1200, 1800, 3000].
        var paliers = SeriesPlanner.PaliersPourSerie(9, 10);

        Assert.Equal(new[] { 600, 1200, 1800, 3000 }, paliers);
    }

    [Fact]
    public void PaliersPourSerie_ProgressionCroissante_EntreSeries()
    {
        var premiere = SeriesPlanner.PaliersPourSerie(0, 10);
        var cinquieme = SeriesPlanner.PaliersPourSerie(4, 10);
        var derniere = SeriesPlanner.PaliersPourSerie(9, 10);

        Assert.True(premiere[3] < cinquieme[3]);
        Assert.True(cinquieme[3] < derniere[3]);
    }

    [Fact]
    public void PickRandomRoundModes_RenvoieLeNombreDemande()
    {
        var modes = SeriesPlanner.PickRandomRoundModes(7);

        Assert.Equal(7, modes.Count);
        Assert.All(modes, mode => Assert.True(Enum.IsDefined(mode)));
    }

    [Fact]
    public void PickRandomRoundModes_SurBeaucoupDEssais_TireLesTroisModes()
    {
        var modes = SeriesPlanner.PickRandomRoundModes(300);

        Assert.Contains(RoundMode.Qcm, modes);
        Assert.Contains(RoundMode.TapeReponse, modes);
        Assert.Contains(RoundMode.PremiereLettre, modes);
    }
}
