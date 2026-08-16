using System.Text.Json;
using Blindify.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Blindify.Infrastructure.Stats;

public class StatsRepository : IStatsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    private readonly string _path;
    private readonly object _lock = new();
    private readonly Dictionary<string, StatsEntryDto> _stats;

    public StatsRepository(IOptions<DataPathsOptions> dataPaths)
    {
        _path = dataPaths.Value.StatsPath;
        _stats = File.Exists(_path)
            ? JsonSerializer.Deserialize<Dictionary<string, StatsEntryDto>>(File.ReadAllText(_path), JsonOptions) ?? []
            : [];
    }

    public int GetPlayCount(string trackId)
    {
        lock (_lock)
        {
            return _stats.TryGetValue(trackId, out var entry) ? entry.PlayCount : 0;
        }
    }

    public void IncrementPlayCount(string trackId)
    {
        lock (_lock)
        {
            if (!_stats.TryGetValue(trackId, out var entry))
            {
                entry = new StatsEntryDto();
                _stats[trackId] = entry;
            }

            entry.PlayCount++;
            File.WriteAllText(_path, JsonSerializer.Serialize(_stats, JsonOptions));
        }
    }
}
