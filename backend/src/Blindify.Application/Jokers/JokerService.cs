using Blindify.Application.Answers;
using Blindify.Application.Rounds;
using Blindify.Domain.Entities;
using Blindify.Domain.Enums;
using Blindify.Domain.Jokers;

namespace Blindify.Application.Jokers;

/// <summary>Voir IJokerService — pur, utilise Random.Shared comme AnneeQcmGenerator. Réutilise
/// RoundService.ReponsesAcceptables (internal, même assembly) pour le texte de référence Titre/Auteur/Film,
/// exactement le même texte que celui validé côté serveur pour une réponse tapée.</summary>
public class JokerService(IAnswerMatcher answerMatcher) : IJokerService
{
    private const int NombreLettresRestantes = 4;
    private static readonly char[] Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();

    public JokerIndice CalculerIndice(RoundMode mode, RoundCible cible, Track track, List<RoundOption>? options, List<int>? anneeOptions)
    {
        if (mode == RoundMode.Qcm)
            return new JokerIndice { OptionsRetirees = ChoisirOptionsARetirer(cible, track, options, anneeOptions) };

        if (mode == RoundMode.PremiereLettre)
            return new JokerIndice { TuilesRestantes = ChoisirTuilesRestantes(cible, track) };

        // TapeReponse
        if (cible == RoundCible.Annee)
            return new JokerIndice { Decennie = (track.Year!.Value / 10) * 10 };

        var texteReference = RoundService.ReponsesAcceptables(cible, track).First();
        return new JokerIndice { Structure = MasquerStructure(texteReference) };
    }

    private static List<string> ChoisirOptionsARetirer(RoundCible cible, Track track, List<RoundOption>? options, List<int>? anneeOptions)
    {
        var mauvaisesValeurs = cible == RoundCible.Annee
            ? (anneeOptions ?? []).Where(a => a != track.Year).Select(a => a.ToString())
            : (options ?? []).Where(o => o.TrackId != track.Id).Select(o => o.OptionId);

        // Retire la moitié des mauvaises valeurs (2 sur 3 avec 4 options) — ne retire jamais la bonne
        // réponse, absente de cette liste par construction. Identifiants opaques d'option (jamais de
        // TrackId côté client, voir RoundOption.OptionId).
        var liste = mauvaisesValeurs.ToList();
        var aRetirer = Math.Max(1, (liste.Count + 1) / 2);
        return [.. liste.OrderBy(_ => Random.Shared.Next()).Take(Math.Min(aRetirer, Math.Max(0, liste.Count - 1)))];
    }

    private List<string> ChoisirTuilesRestantes(RoundCible cible, Track track)
    {
        // Audit 2026-11-10 : prendre toujours la première variante ("24kGoldn, iann dior" -> "2")
        // pouvait retirer la seule tuile valable ("I") et rendre le round impossible. On garde la
        // lettre d'une variante qui commence effectivement par une lettre A-Z.
        var lettreCorrecte = RoundService.ReponsesAcceptables(cible, track)
            .Select(answerMatcher.Normaliser)
            .Where(n => n.Length > 0 && char.IsLetter(n[0]))
            .Select(n => char.ToUpperInvariant(n[0]))
            .FirstOrDefault(c => Alphabet.Contains(c), 'A');

        var autresLettres = Alphabet.Where(l => l != lettreCorrecte).OrderBy(_ => Random.Shared.Next()).Take(NombreLettresRestantes - 1);
        return [.. new[] { lettreCorrecte }.Concat(autresLettres).OrderBy(l => l).Select(l => l.ToString())];
    }

    /// <summary>Masque chaque lettre par un underscore, préserve tout le reste (espaces, ponctuation,
    /// chiffres) — même esprit qu'un pendu, pour donner la longueur/structure des mots sans la réponse.</summary>
    private static string MasquerStructure(string texte) => new([.. texte.Select(c => char.IsLetter(c) ? '_' : c)]);
}
