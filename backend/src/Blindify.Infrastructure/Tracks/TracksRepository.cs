using System.Text.Json;
using Blindify.Domain.Entities;
using Blindify.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Blindify.Infrastructure.Tracks;

public class TracksRepository : ITracksRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<string, Track> _tracksById;
    private readonly List<Track> _tracks;

    public TracksRepository(IOptions<DataPathsOptions> dataPaths)
    {
        var path = dataPaths.Value.TracksPath;

        if (!File.Exists(path))
            throw new FileNotFoundException($"tracks.json introuvable au chemin configuré (Data:TracksPath) : {path}", path);

        var json = File.ReadAllText(path);
        var dtos = JsonSerializer.Deserialize<List<TrackDto>>(json, JsonOptions)
                   ?? throw new InvalidOperationException($"tracks.json est vide ou invalide : {path}");

        _tracks = dtos.Select(d => d.ToDomain()).ToList();
        _tracksById = _tracks.ToDictionary(t => t.Id);
    }

    public IReadOnlyList<Track> GetAll() => _tracks;

    public Track? GetById(string id) => _tracksById.GetValueOrDefault(id);
}
