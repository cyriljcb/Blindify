using Blindify.Api.Logging;
using Microsoft.Extensions.Logging;

namespace Blindify.Tests.Logging;

public class FichierLoggerProviderTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "blindify-logs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dossier)) Directory.Delete(_dossier, recursive: true);
    }

    [Fact]
    public void Log_EcritDansLeFichierDuJour()
    {
        using (var provider = new FichierLoggerProvider(_dossier, retentionJours: 30))
        {
            provider.CreateLogger("Blindify.Test").LogWarning("SubmitAnswer rejeté ({Raison})", "roundId périmé");
        } // Dispose vide la file d'attente avant de rendre la main

        var fichier = Path.Combine(_dossier, $"blindify-{DateTime.Now:yyyy-MM-dd}.log");
        var contenu = File.ReadAllText(fichier);
        Assert.Contains("WARN Blindify.Test — SubmitAnswer rejeté (roundId périmé)", contenu);
    }

    [Fact]
    public void Ouverture_PurgeLesFichiersPlusVieuxQueLaRetention()
    {
        Directory.CreateDirectory(_dossier);
        var ancien = Path.Combine(_dossier, $"blindify-{DateTime.Now.AddDays(-40):yyyy-MM-dd}.log");
        var recent = Path.Combine(_dossier, $"blindify-{DateTime.Now.AddDays(-3):yyyy-MM-dd}.log");
        File.WriteAllText(ancien, "x");
        File.WriteAllText(recent, "x");

        using (var provider = new FichierLoggerProvider(_dossier, retentionJours: 30))
        {
            provider.CreateLogger("Blindify.Test").LogInformation("déclenche l'ouverture du fichier du jour");
        }

        Assert.False(File.Exists(ancien));
        Assert.True(File.Exists(recent));
    }
}
