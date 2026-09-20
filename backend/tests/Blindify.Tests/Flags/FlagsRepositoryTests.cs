using Blindify.Domain.Enums;
using Blindify.Infrastructure.Configuration;
using Blindify.Infrastructure.Flags;
using Microsoft.Extensions.Options;

namespace Blindify.Tests.Flags;

public class FlagsRepositoryTests : IDisposable
{
    private readonly string _flagsPath = Path.Combine(Path.GetTempPath(), $"blindify-flags-{Guid.NewGuid()}.json");
    private readonly string _resolutionsPath = Path.Combine(Path.GetTempPath(), $"blindify-flags-resolutions-{Guid.NewGuid()}.json");

    public void Dispose()
    {
        if (File.Exists(_flagsPath)) File.Delete(_flagsPath);
        if (File.Exists(_resolutionsPath)) File.Delete(_resolutionsPath);
    }

    private IFlagsRepository CreateRepository()
    {
        var options = Options.Create(new DataPathsOptions
        {
            TracksPath = "unused",
            StatsPath = "unused",
            FlagsPath = _flagsPath,
            FlagsResolutionsPath = _resolutionsPath,
            RootPath = "unused",
        });
        return new FlagsRepository(options);
    }

    [Fact]
    public void Ajouter_FichierInexistant_CreeLaPremiereEntree()
    {
        var repo = CreateRepository();

        var (flagId, dejaSignale) = repo.Ajouter("t1", RaisonSignalement.MauvaiseVersion, "version live", "admin", "K4PZ", ["annees-1980"], RoundCible.Titre, RoundMode.Qcm);

        Assert.False(dejaSignale);
        Assert.NotEmpty(flagId);
    }

    [Fact]
    public void Ajouter_MemeMorceauMemeRaisonMemePartie_RetourneLEntreeExistante()
    {
        var repo = CreateRepository();

        var (flagId1, _) = repo.Ajouter("t1", RaisonSignalement.MauvaiseVersion, null, "host", "K4PZ", [], RoundCible.Titre, RoundMode.Qcm);
        var (flagId2, dejaSignale2) = repo.Ajouter("t1", RaisonSignalement.MauvaiseVersion, "commentaire différent", "admin", "K4PZ", [], RoundCible.Auteur, RoundMode.TapeReponse);

        Assert.True(dejaSignale2);
        Assert.Equal(flagId1, flagId2);
    }

    [Fact]
    public void Ajouter_MemeMorceauRaisonDifferente_CreeUneNouvelleEntree()
    {
        var repo = CreateRepository();

        var (flagId1, _) = repo.Ajouter("t1", RaisonSignalement.MauvaiseVersion, null, "host", "K4PZ", [], RoundCible.Titre, RoundMode.Qcm);
        var (flagId2, dejaSignale2) = repo.Ajouter("t1", RaisonSignalement.AudioDefectueux, null, "host", "K4PZ", [], RoundCible.Titre, RoundMode.Qcm);

        Assert.False(dejaSignale2);
        Assert.NotEqual(flagId1, flagId2);
    }

    [Fact]
    public void Ajouter_MemeMorceauMemeRaisonAutrePartie_CreeUneNouvelleEntree()
    {
        var repo = CreateRepository();

        var (flagId1, _) = repo.Ajouter("t1", RaisonSignalement.MauvaiseVersion, null, "host", "K4PZ", [], RoundCible.Titre, RoundMode.Qcm);
        var (flagId2, dejaSignale2) = repo.Ajouter("t1", RaisonSignalement.MauvaiseVersion, null, "host", "AUTR", [], RoundCible.Titre, RoundMode.Qcm);

        Assert.False(dejaSignale2);
        Assert.NotEqual(flagId1, flagId2);
    }

    [Fact]
    public void Ajouter_PersisteSurDisque()
    {
        var repo = CreateRepository();
        repo.Ajouter("t1", RaisonSignalement.MauvaiseVersion, null, "host", "K4PZ", [], RoundCible.Titre, RoundMode.Qcm);

        var nouvelleInstance = CreateRepository();
        var (_, dejaSignale) = nouvelleInstance.Ajouter("t1", RaisonSignalement.MauvaiseVersion, null, "host", "K4PZ", [], RoundCible.Titre, RoundMode.Qcm);

        Assert.True(dejaSignale);
    }

    [Fact]
    public void ObtenirTrackIdsBloquants_FichierInexistant_RetourneVide()
    {
        var repo = CreateRepository();

        Assert.Empty(repo.ObtenirTrackIdsBloquants());
    }

    [Fact]
    public void ObtenirTrackIdsBloquants_RaisonBloquante_EstInclus()
    {
        var repo = CreateRepository();
        repo.Ajouter("t1", RaisonSignalement.PasSaPlace, null, "host", "K4PZ", [], RoundCible.Titre, RoundMode.Qcm);

        Assert.Contains("t1", repo.ObtenirTrackIdsBloquants());
    }

    [Theory]
    [InlineData(RaisonSignalement.HorsTheme)]
    [InlineData(RaisonSignalement.RefrainMalPlace)]
    [InlineData(RaisonSignalement.Autre)]
    public void ObtenirTrackIdsBloquants_RaisonNonBloquante_EstExclu(RaisonSignalement raison)
    {
        var repo = CreateRepository();
        repo.Ajouter("t1", raison, "commentaire", "host", "K4PZ", [], RoundCible.Titre, RoundMode.Qcm);

        Assert.Empty(repo.ObtenirTrackIdsBloquants());
    }

    [Fact]
    public void ObtenirTrackIdsBloquants_IdDejaResoluDansLAutreFichier_EstExclu()
    {
        var repo = CreateRepository();
        var (flagId, _) = repo.Ajouter("t1", RaisonSignalement.PasSaPlace, null, "host", "K4PZ", [], RoundCible.Titre, RoundMode.Qcm);

        File.WriteAllText(_resolutionsPath, $$"""{ "{{flagId}}": { "resolution": "corrige", "date": "2026-10-05" } }""");
        var nouvelleInstance = CreateRepository(); // flags_resolutions.json lu une seule fois, au démarrage

        Assert.Empty(nouvelleInstance.ObtenirTrackIdsBloquants());
    }
}
