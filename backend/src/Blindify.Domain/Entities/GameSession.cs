using Blindify.Domain.Configuration;
using Blindify.Domain.Enums;

namespace Blindify.Domain.Entities;

public class GameSession
{
    public required string Id { get; set; }
    public GameState Etat { get; set; } = GameState.Lobby;
    public bool EnPause { get; set; }
    public DateTimeOffset? PauseDemarreeA { get; set; }
    public bool ModeEquipe { get; set; }
    public required GameConfig Config { get; set; }
    public List<Series> SeriesList { get; set; } = [];
    public List<Player> Players { get; set; } = [];
    public List<Team> Teams { get; set; } = [];
}
