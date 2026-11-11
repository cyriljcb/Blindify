using System.Globalization;
using System.Text;
using Blindify.Domain.Configuration;

namespace Blindify.Application.Answers;

public class AnswerMatcher : IAnswerMatcher
{
    public int DistanceLevenshtein(string a, string b)
    {
        var lenA = a.Length;
        var lenB = b.Length;
        var distances = new int[lenA + 1, lenB + 1];

        for (var i = 0; i <= lenA; i++) distances[i, 0] = i;
        for (var j = 0; j <= lenB; j++) distances[0, j] = j;

        for (var i = 1; i <= lenA; i++)
        {
            for (var j = 1; j <= lenB; j++)
            {
                var cout = a[i - 1] == b[j - 1] ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cout);
            }
        }

        return distances[lenA, lenB];
    }

    public string Normaliser(string texte)
    {
        var sansAccents = RetirerAccents(RemplacerLettresSansDecomposition(texte.ToLowerInvariant()));
        var sb = new StringBuilder(sansAccents.Length);
        var dernierEtaitEspace = false;

        foreach (var c in sansAccents)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                dernierEtaitEspace = false;
            }
            else if (!dernierEtaitEspace && sb.Length > 0)
            {
                sb.Append(' ');
                dernierEtaitEspace = true;
            }
        }

        return sb.ToString().Trim();
    }

    public bool EstCorrecte(string reponseJoueur, string reponseAttendue, GameConfig config) =>
        EstCorrecteInterne(
            reponseJoueur, reponseAttendue,
            config.SeuilToleranceLevenshteinRatio,
            config.LongueurMinimalePourTolerance,
            config.LongueurMinimalePourToleranceFixe,
            config.ToleranceFixeReponseCourte,
            config.LongueurMinimalePourToleranceFixeComplete,
            config.ToleranceFixeReponseTresCourte);

    public bool EstCorrecte(
        string reponseJoueur,
        string reponseAttendue,
        double toleranceRatio,
        int longueurMinimalePourTolerance,
        int longueurMinimalePourToleranceFixe,
        int toleranceFixeReponseCourte) =>
        EstCorrecteInterne(
            reponseJoueur, reponseAttendue, toleranceRatio, longueurMinimalePourTolerance,
            longueurMinimalePourToleranceFixe, toleranceFixeReponseCourte,
            longueurMinimalePourToleranceFixe, toleranceFixeReponseCourte);

    private bool EstCorrecteInterne(
        string reponseJoueur,
        string reponseAttendue,
        double toleranceRatio,
        int longueurMinimalePourTolerance,
        int longueurMinimalePourToleranceFixe,
        int toleranceFixeReponseCourte,
        int longueurMinimalePourToleranceFixeComplete,
        int toleranceFixeReponseTresCourte)
    {
        var normaliseeJoueur = NormaliserPourComparaison(reponseJoueur);

        // Plusieurs écritures acceptées pour le texte attendu (retour utilisateur 2026-11-10) : "&"
        // se lit "et"/"and" (NormaliserPourComparaison) ou s'omet complètement ("Earth Wind Fire").
        // La plus favorable au joueur l'emporte ; le seuil de tolérance se calcule sur chaque
        // variante elle-même.
        var variantes = new[] { NormaliserPourComparaison(reponseAttendue), Normaliser(reponseAttendue) }
            .Where(v => v.Length > 0)
            .Distinct();

        return variantes.Any(attendue => DansLaTolerance(
            normaliseeJoueur, attendue, toleranceRatio, longueurMinimalePourTolerance,
            longueurMinimalePourToleranceFixe, toleranceFixeReponseCourte,
            longueurMinimalePourToleranceFixeComplete, toleranceFixeReponseTresCourte));
    }

    private bool DansLaTolerance(
        string joueur,
        string attendue,
        double toleranceRatio,
        int longueurMinimalePourTolerance,
        int longueurMinimalePourToleranceFixe,
        int toleranceFixeReponseCourte,
        int longueurMinimalePourToleranceFixeComplete,
        int toleranceFixeReponseTresCourte)
    {
        if (attendue.Length < longueurMinimalePourToleranceFixe)
            return joueur == attendue;

        if (attendue.Length < longueurMinimalePourToleranceFixeComplete)
            return DistanceLevenshtein(joueur, attendue) <= toleranceFixeReponseTresCourte;

        if (attendue.Length < longueurMinimalePourTolerance)
            return DistanceLevenshtein(joueur, attendue) <= toleranceFixeReponseCourte;

        var seuil = Math.Max(1, (int)Math.Floor(attendue.Length * toleranceRatio));
        return DistanceLevenshtein(joueur, attendue) <= seuil;
    }

    /// <summary>Normaliser + équivalences de saisie (retour utilisateur 2026-11-10) : "&amp;", "and",
    /// "et" et "+" sont interchangeables ("Earth, Wind &amp; Fire" accepte "earth wind and fire" comme
    /// "earth wind et fire"). Réservé à la comparaison de réponses tapées — jamais utilisé pour la
    /// première lettre (un titre commençant par "And" ne doit pas devenir "e").</summary>
    private string NormaliserPourComparaison(string texte)
    {
        var avecEt = texte.Replace("&", " et ").Replace("+", " et ");
        var mots = Normaliser(avecEt).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(m => m is "and" or "n" ? "et" : m);
        return string.Join(' ', mots);
    }

    /// <summary>Lettres que la décomposition Unicode (FormD) ne ramène pas à une lettre de base —
    /// retour utilisateur 2026-11-10 : "MØ" ne s'écrivait pas au clavier et sa première lettre "Ø" ne
    /// correspondait à aucune tuile A-Z. Appelé sur un texte déjà en minuscules.</summary>
    private static string RemplacerLettresSansDecomposition(string texte)
    {
        if (texte.All(c => c < 128)) return texte;

        var sb = new StringBuilder(texte.Length);
        foreach (var c in texte)
        {
            sb.Append(c switch
            {
                'ø' => "o",
                'œ' => "oe",
                'æ' => "ae",
                'ß' => "ss",
                'ł' => "l",
                'đ' or 'ð' => "d",
                'þ' => "th",
                'ı' => "i",
                'ħ' => "h",
                _ => c.ToString(),
            });
        }
        return sb.ToString();
    }

    private static string RetirerAccents(string texte)
    {
        var normalise = texte.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalise.Length);

        foreach (var c in normalise)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
