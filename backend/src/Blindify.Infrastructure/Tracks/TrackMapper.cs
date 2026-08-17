using Blindify.Domain.Entities;

namespace Blindify.Infrastructure.Tracks;

internal static class TrackMapper
{
    public static Track ToDomain(this TrackDto dto) => new()
    {
        Id = dto.Id,
        Title = dto.Title,
        Artist = dto.Artist,
        Album = dto.Album,
        SpotifyId = dto.SpotifyId,
        YoutubeId = dto.YoutubeId,
        DurationMs = dto.DurationMs,
        Genres = dto.Genres,
        Tags = dto.Tags,
        TrapWith = dto.TrapWith,
        Year = dto.Year,
        FilePath = dto.FilePath,
        CoverPath = dto.CoverPath,
        RefrainStartMs = dto.RefrainStartMs,
        AddedAt = dto.AddedAt
    };
}
