using Blindify.Domain.Entities;

namespace Blindify.Application.Rounds;

/// <summary>Tags "œuvre" : morceaux dont la question naturelle est le film/la série d'origine, pas
/// le titre ni l'artiste crédité — cible forcée à Film (RoundService.ChoisirCible), distracteurs QCM
/// tirés uniquement parmi les morceaux du même tag (RoundService.PoolPourQcm), exclus des thèmes qui
/// ne les demandent pas (RoundService.FiltrerParTagsOuGenres), jamais de question Année
/// (QuotaQuestionsAnnee).
///
/// "disney" : toujours exclusif. "film-serie" (2026-10-28) fonctionne pareil, sauf pour un morceau
/// "double" qui porte aussi d'autres tags (retour utilisateur : Eye of the Tiger, Stayin' Alive… sont
/// aussi connus que leur film) : dans un thème Films & séries il reste une question Film, dans un
/// thème qui le sélectionne par un autre tag (ex. "rock") c'est un morceau classique.</summary>
public static class TagsOeuvre
{
    public const string Disney = "disney";
    public const string FilmSerie = "film-serie";

    public static readonly IReadOnlyList<string> Tous = [Disney, FilmSerie];

    /// <summary>Tag œuvre porté par le morceau ("disney" prioritaire), null s'il n'en porte aucun.</summary>
    public static string? DuMorceau(Track track) =>
        Tous.FirstOrDefault(t => Contient(track.Tags, t));

    public static bool EstTagOeuvre(string tag) => Contient(Tous, tag);

    /// <summary>Morceau "film-serie" portant aussi d'autres tags — jamais "disney".</summary>
    public static bool EstDouble(Track track) =>
        DuMorceau(track) == FilmSerie && track.Tags.Any(t => !EstTagOeuvre(t));

    /// <summary>Morceau qui n'a sa place que dans le thème de son tag œuvre.</summary>
    public static bool EstExclusif(Track track) => DuMorceau(track) is not null && !EstDouble(track);

    /// <summary>Cible Film imposée pour ce morceau dans une série de ce thème : toujours pour un
    /// morceau exclusif (y compris en thème aléatoire), pour un morceau double seulement si le thème
    /// demande "film-serie".</summary>
    public static bool ForceCibleFilm(Track track, IReadOnlyList<string> tagsSerie) =>
        EstExclusif(track) || (EstDouble(track) && Contient(tagsSerie, FilmSerie));

    /// <summary>Le morceau a-t-il sa place dans une série de ce thème (non vide) au regard de ses tags
    /// œuvre ? Un morceau double n'y entre que par un de ses autres tags, jamais par ses seuls
    /// genres Spotify (un "pop" de genre ne fait pas d'un générique de série un morceau pop).</summary>
    public static bool AdmisDansTheme(Track track, IReadOnlyList<string> tagsSerie)
    {
        var tagOeuvre = DuMorceau(track);
        if (tagOeuvre is null || Contient(tagsSerie, tagOeuvre)) return true;
        return EstDouble(track) && track.Tags.Any(t => Contient(tagsSerie, t));
    }

    private static bool Contient(IEnumerable<string> tags, string tag) => tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
}
