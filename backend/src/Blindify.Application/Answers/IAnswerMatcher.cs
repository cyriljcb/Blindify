namespace Blindify.Application.Answers;

/// <summary>Validation des réponses en mode TapeReponse — voir architecture.md section 11 (recommandation Levenshtein).</summary>
public interface IAnswerMatcher
{
    int DistanceLevenshtein(string a, string b);

    string Normaliser(string texte);

    /// <summary>seuil = max(1, floor(longueur(texteNormalisé) × toleranceRatio)).</summary>
    bool EstCorrecte(string reponseJoueur, string reponseAttendue, double toleranceRatio);
}
