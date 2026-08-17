namespace Blindify.Api.Contracts;

public record PlayerSummaryDto(string PlayerId, string Nom, bool EstConnecte, string? TeamId);

/// <summary>Teams : les équipes disponibles (vide si le mode équipe n'est pas actif) — permet au
/// client qui rejoint de proposer un choix sans appel supplémentaire.
/// Joueurs : le roster complet de la partie (soi-même inclus) — sans ça, un joueur qui rejoint
/// après d'autres ne les voyait jamais (PlayerJoined n'est diffusé qu'aux joueurs déjà présents,
/// voir GameHub.JoinGame), y compris lui-même s'il était seul.</summary>
public record JoinGameResultDto(bool Success, string? ErrorMessage, int Score, string? TeamId, List<TeamDto> Teams, List<PlayerSummaryDto> Joueurs);

public record PlayerJoinedDto(string PlayerId, string Nom);

public record PlayerConnectionChangedDto(string PlayerId, bool EstConnecte);

public record PlayerTeamChangedDto(string PlayerId, string TeamId);
