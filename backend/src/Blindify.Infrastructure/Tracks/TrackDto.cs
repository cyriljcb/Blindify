namespace Blindify.Infrastructure.Tracks;

/// <summary>Miroir exact d'une entrée tracks.json (architecture.md section 4), désérialisation JSON.</summary>
public class TrackDto
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Artist { get; set; }
    public string? Album { get; set; }
    public string? SpotifyId { get; set; }
    public string? YoutubeId { get; set; }
    public int DurationMs { get; set; }
    public List<string> Genres { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public List<string> TrapWith { get; set; } = [];
    public int? Year { get; set; }
    public required string FilePath { get; set; }
    public string? CoverPath { get; set; }
    public int? RefrainStartMs { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}
