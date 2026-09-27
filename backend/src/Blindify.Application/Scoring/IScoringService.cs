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

    /// <summary>Scoring à la proximité pour la cible Année en mode saisie (V2, section 12.5) — écart
    /// nul : plein pointsEnJeu ; écart entre 1 et ToleranceAnnee : dégressif linéaire ; au-delà :
    /// pénalité de mauvaise réponse habituelle (PointsMauvaiseReponse). Ne s'applique jamais au mode
    /// Qcm (comparaison stricte via un TrackId... ici un texte d'année, pas de dégressivité).</summary>
    int PointsAnneeApproximative(int pointsEnJeu, int ecartAnnee, SeriesConfig config);
}
