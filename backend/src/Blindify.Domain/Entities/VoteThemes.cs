namespace Blindify.Domain.Entities;

/// <summary>Vote des thèmes par les joueurs au lobby (retour utilisateur 2026-10-17) : chaque joueur
/// choisit jusqu'à VotesParJoueur thèmes parmi ThemesProposes, puis le host fixe le nombre de séries
/// et ConfigurerPartie tire les thèmes joués parmi les thèmes votés (voir
/// SeriesPlanner.ChoisirThemesDepuisVotes). Ouvert repasse à false dès qu'une configuration a été
/// calculée depuis les votes — les votes eux-mêmes sont conservés (réouverture possible).</summary>
public class VoteThemes
{
    public bool Ouvert { get; set; }
    public int VotesParJoueur { get; set; }
    public List<string> ThemesProposes { get; set; } = [];

    /// <summary>PlayerId → thèmes choisis (dédoublonnés, au plus VotesParJoueur).</summary>
    public Dictionary<string, List<string>> VotesParPlayerId { get; set; } = [];

    /// <summary>Thèmes tirés par le dernier ConfigurerPartie(UtiliserVotes), dans l'ordre des séries
    /// (retour utilisateur 2026-10-17 : rendre le tirage visible sur la TV). Null tant qu'aucune
    /// configuration n'a été calculée depuis les votes, ou après une réouverture du vote.</summary>
    public List<string>? ThemesRetenus { get; set; }

    /// <summary>Thèmes ayant reçu au moins une voix, dans l'ordre des thèmes proposés.</summary>
    public List<string> ThemesVotes() =>
        ThemesProposes.Where(t => VotesParPlayerId.Values.Any(v => v.Contains(t))).ToList();
}
