using Blindify.Domain.Configuration;
using Blindify.Domain.Enums;

namespace Blindify.Api.Contracts;

public record TeamDto(string Id, string Nom);

public record SeriesSetupDto(SeriesConfig Config, List<RoundMode> RoundModes);

/// <summary>NomsEquipes : uniquement pris en compte si ModeEquipe est actif — une Team est créée
/// par nom fourni. Ignoré (aucune équipe créée) si ModeEquipe est false.</summary>
public record CreateGameRequestDto(List<string> Tags, bool ModeEquipe, List<SeriesSetupDto> SeriesSetups, GameConfig? Config, List<string>? NomsEquipes = null);

public record CreateGameResultDto(string Code, List<TeamDto> Teams);
