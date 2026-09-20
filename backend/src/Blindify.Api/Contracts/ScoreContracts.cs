namespace Blindify.Api.Contracts;

public record PlayerScoreDto(string PlayerId, string Nom, int Score, string? TeamId);

public record TeamScoreDto(string TeamId, string Nom, int Score);

/// <summary>Classement par équipe présent uniquement si ModeEquipe est actif — voir architecture.md section 8.</summary>
public record ScoreUpdateDto(List<PlayerScoreDto> Joueurs, List<TeamScoreDto>? Equipes);

/// <summary>Titre de fin de partie (V2, section 12.6) — même DTO host/joueurs, aucun secret (Description
/// contient déjà la valeur mesurée, ex. "2,4 s en moyenne").</summary>
public record TitreDto(string Code, string Libelle, string Description, List<string> PlayerIds);

/// <summary>Événement GameEnded — score final identique à ScoreUpdate, plus les titres calculés une seule
/// fois par GameHub.EndGame (voir TitresService.CalculerTitres).</summary>
public record GameEndedDto(ScoreUpdateDto Score, List<TitreDto> Titres);
