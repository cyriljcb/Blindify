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

    /// <summary>Points fixes (négatifs) perdus en cas d'absence de réponse dans le délai — voir architecture.md
    /// section 6. Brièvement passé à -2 en V2 (avec un garde-fou refusant toute valeur plus sévère que
    /// l'espérance d'un clic au hasard en QCM) ; retour utilisateur (2026-09-27) : -2 ne fait pas "pro",
    /// remis à -5 et garde-fou supprimé — on assume qu'un clic au hasard soit légèrement plus rentable
    /// que l'abstention.</summary>
    public int PenaliteAbsenceReponse { get; set; } = -5;

    /// <summary>4 paliers croissants (safe / moyen / moyen+ / risqué) — voir architecture.md section 7.</summary>
    public int[] PaliersDeMise { get; set; } = new int[4];

    public int DureePhaseMiseMs { get; set; }
    public int DureePhaseQuestionMs { get; set; }

    /// <summary>V2, section 12.5 — écart maximal (en années) toléré en mode saisie pour la cible
    /// Année, au-delà duquel une réponse redevient une "mauvaise réponse" classique. Voir
    /// ScoringService.PointsAnneeApproximative.</summary>
    public int ToleranceAnnee { get; set; } = 3;

    /// <summary>Écart maximal toléré pour la question bonus à cible Année — tout ou rien (la mise
    /// est gagnée ou perdue en entier), pas de dégressivité comme pour ToleranceAnnee.</summary>
    public int ToleranceAnneeBonus { get; set; } = 1;
}
