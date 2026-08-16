using System.Globalization;
using System.Text;

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
        var sansAccents = RetirerAccents(texte.ToLowerInvariant());
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

    public bool EstCorrecte(string reponseJoueur, string reponseAttendue, double toleranceRatio)
    {
        var normaliseeAttendue = Normaliser(reponseAttendue);
        var normaliseeJoueur = Normaliser(reponseJoueur);

        var seuil = Math.Max(1, (int)Math.Floor(normaliseeAttendue.Length * toleranceRatio));
        return DistanceLevenshtein(normaliseeJoueur, normaliseeAttendue) <= seuil;
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
