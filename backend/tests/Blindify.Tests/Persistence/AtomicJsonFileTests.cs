using System.Text.Json;
using Blindify.Infrastructure.Persistence;

namespace Blindify.Tests.Persistence;

public class AtomicJsonFileTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"blindify-atomic-{Guid.NewGuid()}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
        if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp");
    }

    [Fact]
    public void Write_FichierInexistant_CreeLeFichierAvecLeContenu()
    {
        AtomicJsonFile.Write(_path, new { valeur = 42 }, new JsonSerializerOptions());

        Assert.True(File.Exists(_path));
        Assert.Contains("42", File.ReadAllText(_path));
    }

    [Fact]
    public void Write_FichierExistant_RemplaceEntierementLeContenu()
    {
        AtomicJsonFile.Write(_path, new { valeur = 1 }, new JsonSerializerOptions());

        AtomicJsonFile.Write(_path, new { valeur = 2 }, new JsonSerializerOptions());

        var contenu = File.ReadAllText(_path);
        Assert.Contains("2", contenu);
        Assert.DoesNotContain("1", contenu);
    }

    [Fact]
    public void Write_NeLaissePasDeFichierTemporaireApresCoup()
    {
        AtomicJsonFile.Write(_path, new { valeur = 1 }, new JsonSerializerOptions());

        Assert.False(File.Exists(_path + ".tmp"));
    }
}
