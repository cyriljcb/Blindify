using System.Text.RegularExpressions;
using Blindify.Domain.Entities;

namespace Blindify.Application.Rounds;

/// <summary>Déduit le nom du film à partir de Track.Title/Track.Album pour la cible RoundCible.Film
/// (morceaux "disney") — les métadonnées Spotify suivent quelques formats récurrents ("X (Original
/// Motion Picture Soundtrack)", "X Original Soundtrack (French Version)", "Chanson - Extrait de
/// "Le Film"") qu'il faut nettoyer pour obtenir une réponse raisonnable à taper/reconnaître.
/// Best-effort : quelques albums de compilation (ex. "La Magie De Disney", regroupant des chansons
/// de plusieurs films sans indiquer lequel) ne permettent de déduire aucun film et resteront
/// incorrects tant que l'`album` n'est pas corrigé à la main dans tracks.json.</summary>
public static partial class FilmNameResolver
{
    private static readonly string[] SuffixesQualificatifs =
    [
        "special edition",
        "original motion picture soundtrack",
        "original soundtrack",
        "soundtrack",
        "ost",
        "deluxe edition",
        "deluxe collection",
    ];

    public static string Resoudre(Track track)
    {
        // Priorité au titre : "Chanson - Extrait de "Le Film"" / "Chanson - De "Le Film"" nomme le
        // film explicitement, plus fiable qu'un album de compilation qui ne le mentionne pas (voir
        // Cerise Calixte dans le catalogue "disney" français, ex. "Il vit en toi - Extrait de "Le
        // roi lion 2"").
        var matchTitre = RegexExtraitDeTitre().Match(track.Title);
        if (matchTitre.Success) return matchTitre.Groups[1].Value.Trim();

        return NettoyerAlbum(track.Album) ?? track.Title;
    }

    private static string? NettoyerAlbum(string? album)
    {
        if (string.IsNullOrWhiteSpace(album)) return null;

        // "X (From "Le Film")" -> le vrai titre du morceau (X) ne correspond à aucun format de
        // réponse utile ici, seul le nom entre guillemets est le film.
        var matchFrom = RegexFrom().Match(album);
        if (matchFrom.Success) return matchFrom.Groups[1].Value.Trim();

        var nettoye = album.Trim();

        // Un album peut cumuler plusieurs qualificatifs d'édition/version ("X: Special Edition
        // Original Soundtrack (French Version)") — on retire couche par couche jusqu'à stabilité :
        // d'abord la parenthèse finale (toujours un qualificatif dans ce catalogue, jamais une
        // partie du nom du film lui-même), puis les suffixes textuels connus.
        while (true)
        {
            var avant = nettoye;

            nettoye = RegexParentheseFinale().Replace(nettoye, "").TrimEnd();

            foreach (var suffixe in SuffixesQualificatifs)
                nettoye = Regex.Replace(nettoye, $@"\s*[:\-]?\s*{Regex.Escape(suffixe)}\s*$", "", RegexOptions.IgnoreCase).TrimEnd();

            if (nettoye == avant) break;
        }

        return nettoye.Length > 0 ? nettoye : album.Trim();
    }

    [GeneratedRegex("(?:Extrait de|De)\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex RegexExtraitDeTitre();

    [GeneratedRegex("\\(From \"([^\"]+)\"\\)", RegexOptions.IgnoreCase)]
    private static partial Regex RegexFrom();

    [GeneratedRegex("\\s*\\([^)]*\\)\\s*$")]
    private static partial Regex RegexParentheseFinale();
}
