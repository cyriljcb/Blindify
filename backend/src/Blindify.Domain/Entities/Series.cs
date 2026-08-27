using Blindify.Domain.Configuration;

namespace Blindify.Domain.Entities;

public class Series
{
    public int Index { get; set; }
    public required SeriesConfig Config { get; set; }

    /// <summary>Thème de CETTE série uniquement (retour utilisateur : "je veux vraiment que la
    /// série deux ne concerne QUE du rock") — vide = tout le catalogue ("aléatoire"), sinon
    /// utilisé tel quel comme filtre (OR sur Tags/Genres, voir RoundService.FiltrerParTagsOuGenres).
    /// Assigné aléatoirement par thème disponible côté host à la création (voir host/app.js), donc
    /// généralement un seul tag ici — mais reste une liste pour réutiliser le filtre OR existant.</summary>
    public List<string> Tags { get; set; } = [];

    public List<Round> Rounds { get; set; } = [];
    public BonusRound? BonusRound { get; set; }
}
