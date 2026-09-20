namespace Blindify.Application.Awards;

/// <summary>Un titre de fin de partie déjà attribué à un ou plusieurs joueurs (V2, section 12.6) —
/// PlayerIds mutable (pas un record positional) : TitresService.CalculerTitres complète l'entrée
/// FIDELE au fil de l'attribution plutôt que de la reconstruire.</summary>
public class TitreResultat
{
    public required string Code { get; init; }
    public required string Libelle { get; init; }

    /// <summary>Contient la valeur mesurée (ex. "2,4 s en moyenne") — pas de secret ici, même DTO
    /// pour host et joueurs.</summary>
    public required string Description { get; init; }

    public List<string> PlayerIds { get; init; } = [];
}
