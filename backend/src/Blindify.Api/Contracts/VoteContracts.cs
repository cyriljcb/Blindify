namespace Blindify.Api.Contracts;

/// <summary>Vote des thèmes (retour utilisateur 2026-10-17) — voir GameHub.OuvrirVoteThemes.
/// NombreRoundsMin : seuls les tags du catalogue ayant au moins ce nombre de morceaux sont proposés
/// (un thème trop maigre ferait échouer ConfigurerPartie après coup).</summary>
public record OuvrirVoteThemesRequestDto(int VotesParJoueur, int NombreRoundsMin = 0);

/// <summary>Diffusé à tout le groupe à l'ouverture du vote.</summary>
public record VoteThemesOuvertDto(int VotesParJoueur, List<string> Themes);

public record VoteThemeCompteDto(string Theme, int Nombre);

/// <summary>Décompte diffusé à tout le groupe à chaque vote (événement VotesThemesMisAJour) — jamais
/// qui a voté quoi, seulement le nombre de voix par thème et qui a déjà voté.
/// ThemesDistincts : nombre de thèmes ayant au moins une voix = nombre maximal de séries jouables.
/// ThemesRetenus : thèmes tirés pour la partie, dans l'ordre des séries (série A en premier) — null
/// tant que la partie n'a pas été configurée depuis les votes.</summary>
public record VotesThemesDto(bool Ouvert, List<VoteThemeCompteDto> Votes, List<string> Votants, int ThemesDistincts,
    List<string>? ThemesRetenus = null);

/// <summary>État du vote renvoyé à un joueur qui (re)rejoint — null si aucun vote n'a été ouvert.
/// MesVotes : ce que CE joueur avait déjà choisi.</summary>
public record VoteThemesEtatJoueurDto(bool Ouvert, int VotesParJoueur, List<string> Themes, List<string> MesVotes);
