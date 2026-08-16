using Blindify.Domain.Configuration;

namespace Blindify.Application.Scoring;

/// <summary>Scoring de la question bonus — voir architecture.md section 7.</summary>
public interface IBonusScoringService
{
    /// <summary>Index du palier "safe" appliqué par défaut si le joueur ne choisit pas dans le délai.</summary>
    int PalierParDefautIndex { get; }

    int ValeurPalier(SeriesConfig config, int palierIndex);

    /// <summary>Juste → +mise. Faux ou absence de réponse (traitée comme fausse) → -mise.</summary>
    int PointsResultat(int mise, bool estCorrecte);
}
