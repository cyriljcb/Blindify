using System.Text.Json;
using System.Text.Json.Serialization;
using Blindify.Domain.Enums;
using Blindify.Infrastructure.Configuration;
using Blindify.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Blindify.Infrastructure.Flags;

public class FlagsRepository : IFlagsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _flagsPath;
    private readonly object _lock = new();
    private readonly List<FlagEntryDto> _flags;

    /// <summary>Chargé une fois au démarrage, comme tracks.json — jamais réécrit par le backend (voir
    /// FlagResolutionDto).</summary>
    private readonly HashSet<string> _idsResolus;

    public FlagsRepository(IOptions<DataPathsOptions> dataPaths)
    {
        _flagsPath = dataPaths.Value.FlagsPath;
        _flags = File.Exists(_flagsPath)
            ? JsonSerializer.Deserialize<List<FlagEntryDto>>(File.ReadAllText(_flagsPath), JsonOptions) ?? []
            : [];

        var resolutionsPath = dataPaths.Value.FlagsResolutionsPath;
        _idsResolus = File.Exists(resolutionsPath)
            ? JsonSerializer.Deserialize<Dictionary<string, FlagResolutionDto>>(File.ReadAllText(resolutionsPath), JsonOptions)?.Keys.ToHashSet() ?? []
            : [];
    }

    public (string FlagId, bool DejaSignale) Ajouter(
        string trackId, RaisonSignalement raison, string? commentaire, string par, string gameCode,
        IReadOnlyList<string> serieTags, RoundCible cible, RoundMode mode)
    {
        lock (_lock)
        {
            var existant = _flags.FirstOrDefault(f => f.TrackId == trackId && f.Raison == raison && f.GameCode == gameCode);
            if (existant is not null) return (existant.Id, true);

            var entree = new FlagEntryDto
            {
                Id = Guid.NewGuid().ToString("N"),
                TrackId = trackId,
                Raison = raison,
                Commentaire = commentaire,
                Par = par,
                GameCode = gameCode,
                SerieTags = [.. serieTags],
                Cible = cible,
                Mode = mode,
                Horodatage = DateTimeOffset.UtcNow,
            };
            _flags.Add(entree);
            AtomicJsonFile.Write(_flagsPath, _flags, JsonOptions);
            return (entree.Id, false);
        }
    }

    public HashSet<string> ObtenirTrackIdsBloquants()
    {
        lock (_lock)
        {
            return _flags
                .Where(f => RaisonSignalementRules.EstBloquante(f.Raison) && !_idsResolus.Contains(f.Id))
                .Select(f => f.TrackId)
                .ToHashSet();
        }
    }
}
