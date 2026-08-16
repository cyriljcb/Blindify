namespace Blindify.Api.Contracts;

public record JoinGameResultDto(bool Success, string? ErrorMessage, int Score, string? TeamId);

public record PlayerJoinedDto(string PlayerId, string Nom);

public record PlayerConnectionChangedDto(string PlayerId, bool EstConnecte);
