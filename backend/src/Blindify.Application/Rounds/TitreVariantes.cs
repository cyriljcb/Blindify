using System.Text.RegularExpressions;

namespace Blindify.Application.Rounds;

/// <summary>Variantes de titre acceptées comme réponse en mode TapeReponse. Un titre contenant un
/// sous-titre entre parenthèses (ex. "Sweat (A La La La La Long)") ne doit pas obliger le joueur à
/// taper la parenthèse en plus du titre principal : on accepte le titre complet (toujours prioritaire)
/// ET, en repli, le titre sans son contenu parenthétique.</summary>
public static partial class TitreVariantes
{
    // Retour utilisateur (playtest 2026-08-24, affiné le 2026-08-27 pour raisonner en caractères
    // plutôt qu'en mots — un titre à mots longs comme "Bohemian Rhapsody" est aussi pénible à
    // taper qu'un titre à mots courts avec plus de mots) : les titres longs sont pénibles à taper
    // en entier. En dessous de SeuilCaracteresTronque, comportement inchangé (titre complet
    // uniquement). Au-delà, une variante tronquée (mots entiers, jusqu'à ~SeuilCaracteresTronque
    // caractères) est acceptée en plus du titre complet. Au-delà de SeuilCaracteresExclusion, le
    // morceau ne doit plus être tiré en cible Titre du tout (voir RoundService/BonusRoundService)
    // — même tronqué, ça reste trop dur à deviner à l'oreille.
    private const int SeuilCaracteresTronque = 20;
    private const int SeuilCaracteresExclusion = 35;

    public static bool EstEligibleCommeCible(string titre) => titre.Length <= SeuilCaracteresExclusion;

    public static IEnumerable<string> Acceptables(string titre)
    {
        yield return titre;

        var sansParentheses = RegexEspacesMultiples()
            .Replace(RegexParentheses().Replace(titre, " "), " ")
            .Trim();

        if (sansParentheses.Length > 0 && sansParentheses != titre)
            yield return sansParentheses;

        var referenceTroncature = sansParentheses.Length > 0 ? sansParentheses : titre;
        if (referenceTroncature.Length > SeuilCaracteresTronque)
        {
            var tronque = TronquerAuxMotsEntiers(referenceTroncature, SeuilCaracteresTronque);
            if (tronque.Length > 0) yield return tronque;
        }
    }

    /// <summary>Prend des mots entiers depuis le début de <paramref name="texte"/> tant que leur
    /// longueur cumulée (espaces compris) ne dépasse pas <paramref name="budgetCaracteres"/> — au
    /// moins un mot est toujours pris, même s'il dépasse seul le budget, pour ne jamais renvoyer
    /// une chaîne vide.</summary>
    private static string TronquerAuxMotsEntiers(string texte, int budgetCaracteres)
    {
        var motsTronques = new List<string>();
        var longueur = 0;

        foreach (var mot in texte.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var prochaineLongueur = longueur + (motsTronques.Count > 0 ? 1 : 0) + mot.Length;
            if (motsTronques.Count > 0 && prochaineLongueur > budgetCaracteres) break;

            motsTronques.Add(mot);
            longueur = prochaineLongueur;
        }

        return string.Join(' ', motsTronques);
    }

    [GeneratedRegex(@"[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex RegexParentheses();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex RegexEspacesMultiples();
}
