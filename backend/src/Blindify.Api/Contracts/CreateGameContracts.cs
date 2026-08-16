using Blindify.Domain.Configuration;
using Blindify.Domain.Enums;

namespace Blindify.Api.Contracts;

public record SeriesSetupDto(SeriesConfig Config, List<RoundMode> RoundModes);

public record CreateGameRequestDto(List<string> Tags, bool ModeEquipe, List<SeriesSetupDto> SeriesSetups, GameConfig? Config);

public record CreateGameResultDto(string Code);
