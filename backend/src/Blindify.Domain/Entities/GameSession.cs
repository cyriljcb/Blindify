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

    /// <summary>Connexions courantes des joueurs authentifiés admin (Player.EstAdmin, via
    /// GameHub.AuthenticateAdmin) — calculé plutôt que stocké : depuis l'audit 2026-11-10 le rôle est
    /// rattaché au joueur, pas à une connexion SignalR qui change à chaque micro-coupure. Les admins
    /// déclenchent pause/reprise/tableau général/fin de partie/télécommande en plus du host web, sans
    /// jamais lui retirer la main ; jamais StartRound/ConfigurerPartie/CreateGame.</summary>
    public List<string> AdminConnectionIds() =>
        [.. Players.Where(p => p.EstAdmin && p.ConnectionId is not null).Select(p => p.ConnectionId!)];

    /// <summary>Refonte UI (lot 3) — dernier état de régie publié par la page host (libellé de
    /// l'action suivante et disponibilité), renvoyé à un admin dès son authentification pour que sa
    /// télécommande soit à jour sans attendre le prochain changement. Voir GameHub.PublierEtatRegie.</summary>
    public string? RegieLibelleAction { get; set; }

    public bool RegieActionDisponible { get; set; }

    /// <summary>Secret opaque généré à CreateGame, distinct du code de partie (public, connu des
    /// joueurs) — exigé par RejoinAsHost pour empêcher n'importe quel client du réseau local
    /// connaissant seulement le code de prendre le contrôle host (pause, override, fin de partie).</summary>
    public required string HostSecret { get; set; }

    /// <summary>Vote des thèmes par les joueurs (retour utilisateur 2026-10-17) — null tant que le host
    /// n'a jamais ouvert de vote dans ce lobby. Voir GameHub.OuvrirVoteThemes/VoterThemes.</summary>
    public VoteThemes? VoteThemes { get; set; }

    public int SerieCouranteIndex { get; set; }

    /// <summary>Dernière action host/joueur sur ce salon — sert à purger les salons abandonnés
    /// (audit 2026-11-10 : les sessions n'étaient jamais retirées, l'app rejoignait
    /// automatiquement un salon zombie au redémarrage). Voir Api.Hubs.SessionCleanupService.</summary>
    public DateTimeOffset DerniereActiviteA { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Index du round courant dans Series[SerieCouranteIndex].Rounds. -1 = aucun round démarré.</summary>
    public int RoundCourantIndex { get; set; } = -1;

    /// <summary>Verrou pris autour de toute mutation de cette session (GameHub + les deux
    /// TimerCoordinators) — plusieurs connexions SignalR et des timers de fond peuvent muter
    /// Players/Reponses/Mises en parallèle sans ça. `lock` ne pouvant pas englober un `await`, seule
    /// la portion synchrone de chaque méthode est verrouillée ; les SendAsync restent hors verrou.</summary>
    public object Lock { get; } = new();
}
