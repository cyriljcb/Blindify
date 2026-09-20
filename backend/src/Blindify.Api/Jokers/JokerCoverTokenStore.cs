using System.Collections.Concurrent;

namespace Blindify.Api.Jokers;

/// <summary>V2, section 12.7 — jetons opaques pour GET /api/joker/cover/{jeton}. Jamais persisté, jamais
/// expiré explicitement : après le reveal, RoundEndedDto.CoverPath expose de toute façon la vraie
/// pochette en clair, donc un jeton qui traînerait ne fuiterait rien de plus.</summary>
public interface IJokerCoverTokenStore
{
    string Emettre(string trackId);
    string? Resoudre(string jeton);
}

public class JokerCoverTokenStore : IJokerCoverTokenStore
{
    private readonly ConcurrentDictionary<string, string> _jetons = new();

    public string Emettre(string trackId)
    {
        var jeton = Guid.NewGuid().ToString("N");
        _jetons[jeton] = trackId;
        return jeton;
    }

    public string? Resoudre(string jeton) => _jetons.GetValueOrDefault(jeton);
}
