using System.Collections.Concurrent;

namespace Blindify.Api.Jokers;

/// <summary>Jetons opaques à usage court donnant accès à la pochette floutée d'un morceau (joker,
/// GET /api/joker/cover/{jeton}) — jamais le TrackId en clair côté téléphone.</summary>
public interface IJokerCoverTokenStore
{
    string Emettre(string trackId);
    string? Resoudre(string jeton);
}

public class JokerCoverTokenStore : IJokerCoverTokenStore
{
    /// <summary>Audit 2026-11-10 : les jetons n'étaient jamais purgés (fuite mémoire sur une longue
    /// soirée). Un jeton ne sert que pendant le round où le joker a été utilisé.</summary>
    private static readonly TimeSpan DureeDeVie = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, (string TrackId, DateTimeOffset EmisA)> _jetons = new();

    public string Emettre(string trackId)
    {
        Purger();
        var jeton = Guid.NewGuid().ToString("N");
        _jetons[jeton] = (trackId, DateTimeOffset.UtcNow);
        return jeton;
    }

    public string? Resoudre(string jeton) =>
        _jetons.TryGetValue(jeton, out var entree) && DateTimeOffset.UtcNow - entree.EmisA < DureeDeVie
            ? entree.TrackId
            : null;

    private void Purger()
    {
        var limite = DateTimeOffset.UtcNow - DureeDeVie;
        foreach (var (jeton, entree) in _jetons)
            if (entree.EmisA < limite) _jetons.TryRemove(jeton, out _);
    }
}
