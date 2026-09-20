using Blindify.Domain.Configuration;

namespace Blindify.Application.Scoring;

/// <summary>Scoring d'un round classique — voir architecture.md section 6.</summary>
public interface IScoringService
{
    /// <summary>
    /// pointsEnJeu(t) = max(min, max - (tempsÉcoulé / duréeFenêtre) × (max - min)),
    /// où tempsÉcoulé = (maintenant - débutRound) - duréeEnPauseMs.
    /// </summary>
    int CalculerPointsEnJeu(DateTimeOffset debutRound, DateTimeOffset maintenant, long dureeEnPauseMs, SeriesConfig config);

    /// <summary>Réponse juste → +pointsEnJeu.</summary>
    int PointsBonneReponse(int pointsEnJeu);

    /// <summary>Réponse fausse → -pointsEnJeu × PenaliteMauvaiseReponseRatio (pénalité réduite, pas symétrique au gain).</summary>
    int PointsMauvaiseReponse(int pointsEnJeu, SeriesConfig config);

    /// <summary>Pas de réponse dans le délai → pénalité fixe (négative), indépendante de pointsEnJeu.</summary>
    int PointsAbsenceReponse(SeriesConfig config);

    /// <summary>Vrai si l'abstention reste au moins aussi coûteuse en espérance qu'un clic au hasard sur un
    /// QCM à 4 options — voir architecture.md section 6 pour la justification de la pénalité asymétrique.
    /// Un clic au hasard a une espérance de -0.5 × PenaliteMauvaiseReponseRatio × PointsMax en pire cas
    /// (aucune option écartée), formulée ici en fonction de PointsMin (le pointsEnJeu minimal, donc le pire
    /// cas pour l'abstention elle-même en fin de fenêtre) : rejette une config où
    /// PenaliteAbsenceReponse ≤ -(0.75 × PenaliteMauvaiseReponseRatio - 0.25) × PointsMin — sinon un
    /// joueur hésitant redevient mathématiquement incité à ne jamais répondre.</summary>
    bool EstPenaliteAbsenceEquitable(int penaliteAbsenceReponse, double penaliteMauvaiseReponseRatio, int pointsMin);

    /// <summary>Scoring à la proximité pour la cible Année en mode saisie (V2, section 12.5) — écart
    /// nul : plein pointsEnJeu ; écart entre 1 et ToleranceAnnee : dégressif linéaire ; au-delà :
    /// pénalité de mauvaise réponse habituelle (PointsMauvaiseReponse). Ne s'applique jamais au mode
    /// Qcm (comparaison stricte via un TrackId... ici un texte d'année, pas de dégressivité).</summary>
    int PointsAnneeApproximative(int pointsEnJeu, int ecartAnnee, SeriesConfig config);
}
