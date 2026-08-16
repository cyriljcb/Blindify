namespace Blindify.Api.Contracts;

public record PlayerScoreDto(string PlayerId, string Nom, int Score, string? TeamId);

public record TeamScoreDto(string TeamId, string Nom, int Score);

/// <summary>Classement par équipe présent uniquement si ModeEquipe est actif — voir architecture.md section 8.</summary>
public record ScoreUpdateDto(List<PlayerScoreDto> Joueurs, List<TeamScoreDto>? Equipes);
