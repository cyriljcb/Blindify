using System.Text.Json;
using Blindify.Infrastructure.Configuration;
using Blindify.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Blindify.Infrastructure.Presets;

public class PresetsRepository : IPresetsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _presetsPath;
    private readonly object _lock = new();
    private readonly List<PresetEntryDto> _presets;

    public PresetsRepository(IOptions<DataPathsOptions> dataPaths)
    {
        // Repli sur RootPath si PresetsPath n'est pas configuré (tests, anciens appsettings) — en
        // production (Docker), data/presets.json est monté fichier par fichier comme stats.json : sans
        // Data__PresetsPath explicite, les écritures resteraient dans le conteneur et seraient perdues.
        _presetsPath = dataPaths.Value.PresetsPath ?? Path.Combine(dataPaths.Value.RootPath, "presets.json");
        _presets = File.Exists(_presetsPath)
            ? JsonSerializer.Deserialize<List<PresetEntryDto>>(File.ReadAllText(_presetsPath), JsonOptions) ?? []
            : [];
    }

    public IReadOnlyList<PresetEntryDto> Lister()
    {
        lock (_lock)
        {
            return _presets.OrderBy(p => p.Nom, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    public void Enregistrer(PresetEntryDto preset)
    {
        lock (_lock)
        {
            _presets.RemoveAll(p => string.Equals(p.Nom, preset.Nom, StringComparison.CurrentCultureIgnoreCase));
            _presets.Add(preset);
            AtomicJsonFile.Write(_presetsPath, _presets, JsonOptions);
        }
    }

    public bool Supprimer(string nom)
    {
        lock (_lock)
        {
            if (_presets.RemoveAll(p => string.Equals(p.Nom, nom, StringComparison.CurrentCultureIgnoreCase)) == 0)
                return false;
            AtomicJsonFile.Write(_presetsPath, _presets, JsonOptions);
            return true;
        }
    }
}
