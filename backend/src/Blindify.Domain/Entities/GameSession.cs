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

    /// <summary>Tags/genres utilisés pour filtrer le pool à CreateGame — réutilisés pour sélectionner le morceau bonus.</summary>
    public List<string> Tags { get; set; } = [];

    public List<Series> SeriesList { get; set; } = [];
    public List<Player> Players { get; set; } = [];
    public List<Team> Teams { get; set; } = [];

    /// <summary>ConnectionId SignalR courant du client host (mutable, réassocié à chaque RejoinAsHost).</summary>
    public string? HostConnectionId { get; set; }

    public int SerieCouranteIndex { get; set; }

    /// <summary>Index du round courant dans Series[SerieCouranteIndex].Rounds. -1 = aucun round démarré.</summary>
    public int RoundCourantIndex { get; set; } = -1;
}
