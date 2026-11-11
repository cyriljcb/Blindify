using Blindify.Domain.Entities;

namespace Blindify.Application.Rounds;

/// <summary>Un morceau peut avoir plusieurs auteurs crédités — n'importe lequel d'entre eux est une
/// réponse valable (featurings, voir retour utilisateur : exiger la liste complète est ingérable dès
/// qu'il y a plus d'un featuring). Partagé entre RoundService (round classique), BonusRoundService
/// (question bonus), QcmGenerator (dédoublonnage des artistes) et l'affichage QCM (premier auteur).
///
/// Source : Track.Artists quand il est renseigné (audit 2026-11-10), sinon découpage historique de
/// Track.Artist par virgule — ce repli coupe en deux "Earth, Wind &amp; Fire", d'où le champ dédié
/// rempli par data/scripts/migrer_artists.py et les scripts d'import.</summary>
public static class AuteurVariantes
{
    public static IReadOnlyList<string> Acceptables(Track track)
    {
        var credites = track.Artists.Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
        return credites.Count > 0 ? credites : Decouper(track.Artist);
    }

    /// <summary>Premier artiste crédité — texte affiché en QCM cible Auteur.</summary>
    public static string Premier(Track track)
    {
        var acceptables = Acceptables(track);
        return acceptables.Count > 0 ? acceptables[0] : track.Artist.Trim();
    }

    private static List<string> Decouper(string artist) =>
        [.. artist.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
}
