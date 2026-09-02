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

    /// <summary>ConnectionId SignalR courant du client host (mutable, réassocié à chaque RejoinAsHost).</summary>
    public string? HostConnectionId { get; set; }

    /// <summary>Secret opaque généré à CreateGame, distinct du code de partie (public, connu des
    /// joueurs) — exigé par RejoinAsHost pour empêcher n'importe quel client du réseau local
    /// connaissant seulement le code de prendre le contrôle host (pause, override, fin de partie).</summary>
    public required string HostSecret { get; set; }

    public int SerieCouranteIndex { get; set; }

    /// <summary>Index du round courant dans Series[SerieCouranteIndex].Rounds. -1 = aucun round démarré.</summary>
    public int RoundCourantIndex { get; set; } = -1;

    /// <summary>Verrou pris autour de toute mutation de cette session (GameHub + les deux
    /// TimerCoordinators) — plusieurs connexions SignalR et des timers de fond peuvent muter
    /// Players/Reponses/Mises en parallèle sans ça. `lock` ne pouvant pas englober un `await`, seule
    /// la portion synchrone de chaque méthode est verrouillée ; les SendAsync restent hors verrou.</summary>
    public object Lock { get; } = new();
}
