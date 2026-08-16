namespace Blindify.Domain.Configuration;

/// <summary>
/// Paramètres instanciés par série (jamais de constantes en dur) — voir architecture.md section 11.
/// </summary>
public class SeriesConfig
{
    public int NombreRoundsClassiques { get; set; }
    public int DureeFenetreReponseMs { get; set; }

    /// <summary>pointsEnJeu au tout début de la fenêtre de réponse (t=0) — voir architecture.md section 6.</summary>
    public int PointsMax { get; set; }

    /// <summary>pointsEnJeu plancher, atteint quand la fenêtre de réponse est écoulée — voir architecture.md section 6.</summary>
    public int PointsMin { get; set; }

    /// <summary>Ratio appliqué à pointsEnJeu pour la pénalité d'une mauvaise réponse (0.5 par défaut, pas symétrique au gain) — voir architecture.md section 6.</summary>
    public double PenaliteMauvaiseReponseRatio { get; set; } = 0.5;

    /// <summary>Points fixes (négatifs) perdus en cas d'absence de réponse dans le délai — voir architecture.md section 6.</summary>
    public int PenaliteAbsenceReponse { get; set; } = -5;

    /// <summary>4 paliers croissants (safe / moyen / moyen+ / risqué) — voir architecture.md section 7.</summary>
    public int[] PaliersDeMise { get; set; } = new int[4];

    public int DureePhaseMiseMs { get; set; }
    public int DureePhaseQuestionMs { get; set; }
}
