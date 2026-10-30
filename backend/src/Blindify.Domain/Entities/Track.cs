namespace Blindify.Domain.Entities;

/// <summary>
/// Morceau du catalogue — miroir du schéma tracks.json (architecture.md section 4).
/// tracks.json reste la source de vérité ; cette entité représente un morceau une fois chargé en mémoire.
/// </summary>
public class Track
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Artist { get; set; }
    public string? Album { get; set; }

    /// <summary>Nom du film/de la série dont est tiré le morceau, saisi à la main — prioritaire sur
    /// la déduction depuis Album (voir FilmNameResolver) pour la cible Film. Indispensable pour le
    /// tag "film-serie", où l'album est rarement la BO (ex. Friends -> album "L.P." des
    /// Rembrandts) ; facultatif pour "disney", où il corrige les albums de compilation.</summary>
    public string? Oeuvre { get; set; }
    public string? SpotifyId { get; set; }
    public string? YoutubeId { get; set; }
    public int DurationMs { get; set; }
    public List<string> Genres { get; set; } = [];
    public List<string> Tags { get; set; } = [];

    /// <summary>IDs de morceaux fréquemment confondus, utilisés pour générer des QCM pièges.</summary>
    public List<string> TrapWith { get; set; } = [];

    /// <summary>Leurre texte inventé pour la cible Auteur (ex. Bastille - Pompéi -> "Baptiste") :
    /// nom d'artiste plausible mais fictif, écrit à la main, sans rapport avec un morceau réel du
    /// catalogue — voir GameConfig.ProbabiliteQcmFeinteTexteArtiste.</summary>
    public string? TrapTextArtist { get; set; }

    public int? Year { get; set; }
    public required string FilePath { get; set; }
    public string? CoverPath { get; set; }

    /// <summary>Point de départ (ms) du refrain, joué à la place du début du morceau pendant le
    /// round — null = comportement actuel (lecture depuis le début), à renseigner manuellement.</summary>
    public int? RefrainStartMs { get; set; }

    public DateTimeOffset AddedAt { get; set; }
}
