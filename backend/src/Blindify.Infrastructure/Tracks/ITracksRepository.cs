using Blindify.Domain.Entities;

namespace Blindify.Infrastructure.Tracks;

/// <summary>
/// tracks.json chargé en mémoire au démarrage — source de vérité unique, jamais réécrit par le backend
/// (monté :ro dans le conteneur) — voir architecture.md section 4.
/// </summary>
public interface ITracksRepository
{
    IReadOnlyList<Track> GetAll();
    Track? GetById(string id);
}
