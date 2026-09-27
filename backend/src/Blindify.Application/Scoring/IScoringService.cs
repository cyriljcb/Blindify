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
    /// pénalité proportionnelle à l'écart, jusqu'à PenaliteAnneeMaxRatio × pointsEnJeu atteint à
    /// ToleranceAnnee + EcartAnneePenaliteMax années (retour utilisateur 2026-09-27). Ne s'applique
    /// jamais au mode Qcm (comparaison stricte du texte d'année, juste/faux) ni à la question bonus
    /// (tout ou rien, voir ToleranceAnneeBonus).</summary>
    int PointsAnneeApproximative(int pointsEnJeu, int ecartAnnee, SeriesConfig config);
}
