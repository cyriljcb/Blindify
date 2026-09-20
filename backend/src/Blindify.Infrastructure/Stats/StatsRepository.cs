using System.Text.Json;
using Blindify.Domain.Statistics;
using Blindify.Infrastructure.Configuration;
using Blindify.Infrastructure.Persistence;
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
            AtomicJsonFile.Write(_path, _stats, JsonOptions);
        }
    }

    public void EnregistrerResultatsRound(RoundStatsUpdate update)
    {
        lock (_lock)
        {
            if (!_stats.TryGetValue(update.TrackId, out var entry))
            {
                entry = new StatsEntryDto();
                _stats[update.TrackId] = entry;
            }

            if (!entry.Reponses.TryGetValue(update.CleModeCible, out var reponseStat))
            {
                reponseStat = new ReponseStatDto();
                entry.Reponses[update.CleModeCible] = reponseStat;
            }

            reponseStat.N += update.N;
            reponseStat.Correct += update.Correct;
            reponseStat.Absent += update.Absent;
            reponseStat.TempsCorrectCumulMs += update.TempsCorrectCumulMs;
            if (update.EcartCumul is not null)
                reponseStat.EcartCumul = (reponseStat.EcartCumul ?? 0) + update.EcartCumul;

            foreach (var confusion in update.Confusions)
            {
                if (!entry.Confusions.TryGetValue(confusion.TrackId, out var confusionStat))
                {
                    confusionStat = new ConfusionStatDto();
                    entry.Confusions[confusion.TrackId] = confusionStat;
                }

                confusionStat.Presente += confusion.Presente;
                confusionStat.Choisi += confusion.Choisi;
            }

            AtomicJsonFile.Write(_path, _stats, JsonOptions);
        }
    }
}
