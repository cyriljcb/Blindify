namespace Blindify.Domain.Configuration;

/// <summary>
/// Paramètres globaux à la partie (jamais de constantes en dur) — voir architecture.md section 11.
/// </summary>
public class GameConfig
{
    public double ProbabiliteQcmPiege { get; set; } = 0.15;

    /// <summary>Ratio utilisé dans seuil = max(1, floor(longueur(texteNormalisé) × ratio)).</summary>
    public double SeuilToleranceLevenshteinRatio { get; set; } = 0.2;

    public bool RalentissementBonusActive { get; set; } = true;
    public double FacteurRalentissementBonus { get; set; } = 0.8;
}
