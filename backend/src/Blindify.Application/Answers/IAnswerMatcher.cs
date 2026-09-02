namespace Blindify.Application.Answers;

/// <summary>Validation des réponses en mode TapeReponse — voir architecture.md section 11 (recommandation Levenshtein).</summary>
public interface IAnswerMatcher
{
    int DistanceLevenshtein(string a, string b);

    string Normaliser(string texte);

    /// <summary>Trois zones selon la longueur du texte attendu normalisé (voir GameConfig) :
    /// en dessous de longueurMinimalePourToleranceFixe, réponse exacte exigée ; entre ce seuil et
    /// longueurMinimalePourTolerance, écart toléré fixe de toleranceFixeReponseCourte caractères ;
    /// au-delà, seuil = max(1, floor(longueur(texteNormalisé) × toleranceRatio)).</summary>
    bool EstCorrecte(
        string reponseJoueur,
        string reponseAttendue,
        double toleranceRatio,
        int longueurMinimalePourTolerance,
        int longueurMinimalePourToleranceFixe,
        int toleranceFixeReponseCourte);
}
