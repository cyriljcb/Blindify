namespace Blindify.Api.Contracts;

/// <summary>Teams : les équipes disponibles (vide si le mode équipe n'est pas actif) — permet au
/// client qui rejoint de proposer un choix sans appel supplémentaire.</summary>
public record JoinGameResultDto(bool Success, string? ErrorMessage, int Score, string? TeamId, List<TeamDto> Teams);

public record PlayerJoinedDto(string PlayerId, string Nom);

public record PlayerConnectionChangedDto(string PlayerId, bool EstConnecte);

public record PlayerTeamChangedDto(string PlayerId, string TeamId);
