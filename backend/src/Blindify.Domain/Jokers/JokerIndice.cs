namespace Blindify.Domain.Jokers;

/// <summary>Indice obtenu par un joueur ayant utilisé son joker sur un round (V2, section 12.7) — un seul
/// type couvre les 5 combinaisons mode/cible possibles, chacune ne renseignant que le(s) champ(s) qui la
/// concernent. Mutable : <see cref="CoverToken"/> est ajouté après coup par GameHub (le jeton dépend d'un
/// service HTTP, hors de portée de Blindify.Application où l'indice est calculé).</summary>
public class JokerIndice
{
    /// <summary>Qcm, toute cible — 2 des 3 mauvaises valeurs à retirer. Ce sont soit des TrackId
    /// (Round.Options), soit des années en texte (Round.AnneeOptions si Cible==Annee) : dans les deux cas,
    /// exactement les valeurs que le client passerait déjà à SubmitAnswer.</summary>
    public List<string>? OptionsRetirees { get; set; }

    /// <summary>PremiereLettre, Titre/Auteur — les 4 lettres qui restent sélectionnables (dont la bonne).</summary>
    public List<string>? TuilesRestantes { get; set; }

    /// <summary>TapeReponse, Titre/Auteur/Film — texte de référence masqué lettre par lettre (espaces/
    /// ponctuation/chiffres préservés).</summary>
    public string? Structure { get; set; }

    /// <summary>TapeReponse, Annee — décennie de sortie (ex. 1980).</summary>
    public int? Decennie { get; set; }

    /// <summary>Jeton à usage scopé pour GET /api/joker/cover/{jeton} — renseigné uniquement pour
    /// TapeReponse + Titre/Auteur, après coup par GameHub.UtiliserJoker (jamais par JokerService).</summary>
    public string? CoverToken { get; set; }
}
