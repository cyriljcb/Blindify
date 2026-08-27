using System.Text.RegularExpressions;

namespace Blindify.Application.Rounds;

/// <summary>Variantes de titre acceptées comme réponse en mode TapeReponse. Un titre contenant un
/// sous-titre entre parenthèses (ex. "Sweat (A La La La La Long)") ne doit pas obliger le joueur à
/// taper la parenthèse en plus du titre principal : on accepte le titre complet (toujours prioritaire)
/// ET, en repli, le titre sans son contenu parenthétique.</summary>
public static partial class TitreVariantes
{
    // Retour utilisateur (playtest 2026-08-24) : les titres longs ("Another One Bites The Dust")
    // sont pénibles à taper en entier. En dessous de SeuilMotsTronque, comportement inchangé
    // (titre complet uniquement). Entre les deux seuils, les NombreMotsTronques premiers mots sont
    // acceptés en plus du titre complet. Au-delà de SeuilMotsExclusion, le morceau ne doit plus
    // être tiré en cible Titre du tout (voir RoundService/BonusRoundService) — même tronqué, ça
    // reste trop dur à deviner à l'oreille.
    private const int SeuilMotsTronque = 4;
    private const int SeuilMotsExclusion = 7;
    private const int NombreMotsTronques = 3;

    public static bool EstEligibleCommeCible(string titre) => CompterMots(titre) <= SeuilMotsExclusion;

    public static IEnumerable<string> Acceptables(string titre)
    {
        yield return titre;

        var sansParentheses = RegexEspacesMultiples()
            .Replace(RegexParentheses().Replace(titre, " "), " ")
            .Trim();

        if (sansParentheses.Length > 0 && sansParentheses != titre)
            yield return sansParentheses;

        var referenceTroncature = sansParentheses.Length > 0 ? sansParentheses : titre;
        if (CompterMots(referenceTroncature) > SeuilMotsTronque)
        {
            var tronque = string.Join(' ', referenceTroncature.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(NombreMotsTronques));
            if (tronque.Length > 0) yield return tronque;
        }
    }

    private static int CompterMots(string texte) => texte.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    [GeneratedRegex(@"[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex RegexParentheses();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex RegexEspacesMultiples();
}
