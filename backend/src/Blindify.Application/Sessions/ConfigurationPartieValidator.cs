using Blindify.Domain.Configuration;

namespace Blindify.Application.Sessions;

/// <summary>Bornes d'une configuration de partie (audit 2026-11-10) — ConfigurerPartie acceptait
/// n'importe quelles valeurs : une fenêtre de réponse à 0 divisait par zéro dans le scoring et
/// terminait le round immédiatement, PointsMin &gt; PointsMax inversait la dégressivité, des paliers
/// mal formés faisaient planter la question bonus. Renvoie le premier problème trouvé, null si la
/// configuration est jouable.</summary>
public static class ConfigurationPartieValidator
{
    public const int NombreSeriesMax = 26; // séries libellées A à Z
    public const int NombreRoundsMax = 100;
    public const int DureeMinMs = 100;
    public const int DureeMaxMs = 600_000;

    public static string? Valider(
        GameConfig config,
        int nombreSeries,
        int nombreRoundsClassiques,
        int dureeFenetreReponseMs,
        int pointsMax,
        int pointsMin,
        double penaliteMauvaiseReponseRatio,
        int penaliteAbsenceReponse,
        int dureePhaseMiseMs,
        int dureePhaseQuestionMs)
    {
        if (nombreSeries < 1 || nombreSeries > NombreSeriesMax)
            return $"Le nombre de séries doit être compris entre 1 et {NombreSeriesMax}.";
        if (nombreRoundsClassiques < 0 || nombreRoundsClassiques > NombreRoundsMax)
            return $"Le nombre de rounds par série doit être compris entre 0 et {NombreRoundsMax}.";
        if (!DureeValide(dureeFenetreReponseMs) || !DureeValide(dureePhaseMiseMs) || !DureeValide(dureePhaseQuestionMs))
            return $"Les durées doivent être comprises entre {DureeMinMs} ms et {DureeMaxMs / 1000} s.";
        if (pointsMin < 0 || pointsMax < 1 || pointsMin > pointsMax)
            return "Les points doivent vérifier 0 ≤ points min ≤ points max (et points max ≥ 1).";
        if (double.IsNaN(penaliteMauvaiseReponseRatio) || penaliteMauvaiseReponseRatio < 0 || penaliteMauvaiseReponseRatio > 2)
            return "La pénalité d'une mauvaise réponse doit être comprise entre 0 et 2 fois les points en jeu.";
        if (penaliteAbsenceReponse > 0)
            return "La pénalité d'absence doit être négative ou nulle.";

        if (config.PaliersMiseBase is not { Length: 4 } || config.PaliersMiseBase.Any(p => p <= 0))
            return "Il faut exactement 4 paliers de mise strictement positifs.";
        if (config.NombreDistracteursQcm < 1 || config.NombreDistracteursQcm > 5)
            return "Le nombre de distracteurs QCM doit être compris entre 1 et 5.";
        if (config.FacteurProgressionPaliers <= 0 || double.IsNaN(config.FacteurProgressionPaliers))
            return "Le facteur de progression des paliers doit être strictement positif.";
        if (config.SeuilToleranceLevenshteinRatio < 0 || config.SeuilToleranceLevenshteinRatio >= 1)
            return "Le ratio de tolérance des réponses doit être compris entre 0 et 1.";
        if (config.PoidsCibleTitre < 0 || config.PoidsCibleAuteur < 0 || config.PoidsCibleAnnee < 0)
            return "Les poids des cibles ne peuvent pas être négatifs.";

        return null;
    }

    private static bool DureeValide(int dureeMs) => dureeMs >= DureeMinMs && dureeMs <= DureeMaxMs;
}
