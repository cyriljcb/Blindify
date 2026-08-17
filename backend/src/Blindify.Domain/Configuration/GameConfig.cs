namespace Blindify.Domain.Configuration;

/// <summary>
/// Paramètres globaux à la partie (jamais de constantes en dur) — voir architecture.md section 11.
/// </summary>
public class GameConfig
{
    public double ProbabiliteQcmPiege { get; set; } = 0.15;

    /// <summary>Retour utilisateur : piège purement visuel, distinct de ProbabiliteQcmPiege (qui
    /// pioche un VRAI morceau souvent confondu, trapWith). Ici, un des distracteurs affiche le
    /// champ opposé du morceau correct (ex. cible Titre -> une option affiche l'auteur du morceau
    /// correct comme s'il s'agissait d'un titre). L'ID du distracteur ne change pas, donc le
    /// cliquer reste compté comme une mauvaise réponse normalement.</summary>
    public double ProbabiliteQcmFeinteChamp { get; set; } = 0.10;

    /// <summary>Ratio utilisé dans seuil = max(1, floor(longueur(texteNormalisé) × ratio)).</summary>
    public double SeuilToleranceLevenshteinRatio { get; set; } = 0.2;

    public bool RalentissementBonusActive { get; set; } = true;

    /// <summary>Retour utilisateur : 0.8 pas assez perceptible, encore ralenti.</summary>
    public double FacteurRalentissementBonus { get; set; } = 0.65;
}
