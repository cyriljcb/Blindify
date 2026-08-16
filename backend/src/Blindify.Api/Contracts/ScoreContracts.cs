namespace Blindify.Api.Contracts;

public record PlayerScoreDto(string PlayerId, string Nom, int Score, string? TeamId);
