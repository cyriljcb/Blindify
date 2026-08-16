using Blindify.Api.Contracts;
using Blindify.Application.Bonus;
using Blindify.Application.Rounds;
using Blindify.Application.Sessions;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;
using Blindify.Domain.Enums;
using Blindify.Infrastructure.Tracks;
using Microsoft.AspNetCore.SignalR;

namespace Blindify.Api.Hubs;

/// <summary>
/// Hub SignalR — contrat complet dans architecture.md section 10 : lobby, cycle de vie du round classique,
/// question bonus (mise + question ralentie), pause, reconnexion, tableau général, override manuel.
/// Les nuances fines du mode équipe (au-delà de l'agrégation de score) restent à affiner à l'usage.
/// </summary>
public class GameHub(
    IGameSessionStore sessionStore,
    IGameCodeGenerator codeGenerator,
    IRoundService roundService,
    IBonusRoundService bonusRoundService,
    ITracksRepository tracksRepository,
    RoundTimerCoordinator timerCoordinator,
    BonusTimerCoordinator bonusTimerCoordinator) : Hub
{
    // ----- Méthodes host -----

    public async Task<CreateGameResultDto> CreateGame(CreateGameRequestDto request)
    {
        var catalogue = tracksRepository.GetAll();
        var config = request.Config ?? new GameConfig();
        var dejaUtilises = new HashSet<string>();
        var seriesList = new List<Series>();

        foreach (var setup in request.SeriesSetups)
        {
            if (setup.RoundModes.Count != setup.Config.NombreRoundsClassiques)
                throw new HubException("RoundModes doit contenir exactement NombreRoundsClassiques entrées.");

            var morceaux = roundService.SelectionnerMorceaux(catalogue, request.Tags, setup.Config.NombreRoundsClassiques, dejaUtilises);
            if (morceaux.Count < setup.Config.NombreRoundsClassiques)
                throw new HubException("Pas assez de morceaux disponibles dans le catalogue pour cette série.");

            var rounds = morceaux.Select((track, i) => new Round { TrackId = track.Id, Mode = setup.RoundModes[i] }).ToList();
            seriesList.Add(new Series { Index = seriesList.Count, Config = setup.Config, Rounds = rounds });
        }

        string code;
        do { code = codeGenerator.GenererCode(); } while (sessionStore.Exists(code));

        var session = new GameSession
        {
            Id = code,
            Etat = GameState.Lobby,
            ModeEquipe = request.ModeEquipe,
            Config = config,
            Tags = request.Tags,
            SeriesList = seriesList,
            HostConnectionId = Context.ConnectionId
        };

        sessionStore.Add(session);
        sessionStore.AssocierConnexion(Context.ConnectionId, code);
        await Groups.AddToGroupAsync(Context.ConnectionId, code);

        return new CreateGameResultDto(code);
    }

    public async Task<HostStateSnapshotDto> RejoinAsHost(string code)
    {
        var session = sessionStore.Get(code) ?? throw new HubException("Partie introuvable.");

        session.HostConnectionId = Context.ConnectionId;
        sessionStore.AssocierConnexion(Context.ConnectionId, code);
        await Groups.AddToGroupAsync(Context.ConnectionId, code);

        var round = session.RoundCourant();
        if (round?.DebutRound is null)
            return new HostStateSnapshotDto(session.EnPause, null, null, null, null, null);

        var track = tracksRepository.GetById(round.TrackId);
        var positionMs = Math.Max(0, CalculerTempsEcouleMs(session, round));

        return new HostStateSnapshotDto(
            session.EnPause,
            round.Mode,
            track?.Id,
            track?.FilePath,
            positionMs,
            session.SerieCourante().Config.DureeFenetreReponseMs);
    }

    public async Task StartRound()
    {
        var session = ResoudreSessionHost();

        if (session.RoundCourantIndex == -1) session.RoundCourantIndex = 0;

        var serie = session.SerieCourante();
        if (session.RoundCourantIndex >= serie.Rounds.Count)
            throw new HubException("Plus de round classique à démarrer dans cette série.");

        var round = serie.Rounds[session.RoundCourantIndex];
        var track = tracksRepository.GetById(round.TrackId) ?? throw new HubException("Morceau introuvable dans le catalogue.");

        roundService.DemarrerRound(round, track, tracksRepository.GetAll(), session.Config, DateTimeOffset.UtcNow);
        session.Etat = GameState.EnCours;

        var qcmOptions = round.QcmOptionTrackIds?
            .Select(id => tracksRepository.GetById(id))
            .Where(t => t is not null)
            .Select(t => new QcmOptionDto(t!.Id, t.Title, t.Artist))
            .ToList();

        if (session.HostConnectionId is not null)
        {
            await Clients.Client(session.HostConnectionId)
                .SendAsync("RoundStarted", new RoundStartedForHostDto(round.Mode, track.Id, track.FilePath, serie.Config.DureeFenetreReponseMs));
        }

        var joueursConnectes = session.Players.Where(p => p.ConnectionId is not null).Select(p => p.ConnectionId!).ToList();
        await Clients.Clients(joueursConnectes)
            .SendAsync("RoundStarted", new RoundStartedForPlayersDto(round.Mode, serie.Config.DureeFenetreReponseMs, qcmOptions));

        timerCoordinator.DemarrerSurveillance(session.Id, serie.Config);
    }

    public async Task StartBonusRound()
    {
        var session = ResoudreSessionHost();
        var serie = session.SerieCourante();

        if (serie.BonusRound is not null)
            throw new HubException("La question bonus de cette série a déjà été démarrée.");

        var dejaUtilises = session.SeriesList
            .SelectMany(s => s.Rounds.Select(r => r.TrackId))
            .Concat(session.SeriesList.Where(s => s.BonusRound is not null).Select(s => s.BonusRound!.TrackId))
            .ToHashSet();

        var morceaux = roundService.SelectionnerMorceaux(tracksRepository.GetAll(), session.Tags, 1, dejaUtilises);
        if (morceaux.Count == 0)
            throw new HubException("Pas assez de morceaux disponibles pour la question bonus.");

        var bonusRound = bonusRoundService.CreerBonusRound(morceaux[0]);
        serie.BonusRound = bonusRound;
        bonusRoundService.DemarrerPhaseMise(bonusRound, DateTimeOffset.UtcNow);

        await Clients.Group(session.Id).SendAsync("BonusStakeOptions", new BonusStakeOptionsDto(serie.Config.PaliersDeMise, serie.Config.DureePhaseMiseMs));

        bonusTimerCoordinator.DemarrerSurveillance(session.Id, serie.Config);
    }

    public Task NextRound()
    {
        var session = ResoudreSessionHost();
        var serie = session.SerieCourante();

        if (session.RoundCourantIndex + 1 < serie.Rounds.Count)
        {
            session.RoundCourantIndex++;
        }
        else if (session.SerieCouranteIndex + 1 < session.SeriesList.Count)
        {
            session.SerieCouranteIndex++;
            session.RoundCourantIndex = -1;
        }
        // Sinon : dernière série épuisée — le host doit appeler StartBonusRound() puis EndGame().

        return Task.CompletedTask;
    }

    public async Task ShowLeaderboard()
    {
        var session = ResoudreSessionHost();
        await Clients.Group(session.Id).SendAsync("LeaderboardShown", ScoreDtoBuilder.Construire(session));
    }

    public async Task ValidateAnswerManually(ValidateAnswerManuallyRequestDto request)
    {
        var session = ResoudreSessionHost();
        var round = session.RoundCourant() ?? throw new HubException("Aucun round en cours.");
        var serie = session.SerieCourante();

        var resultat = roundService.ValiderManuellement(session, round, serie.Config, request.PlayerId, request.EstCorrecte)
                       ?? throw new HubException("Aucune réponse enregistrée pour ce joueur sur ce round.");

        await Clients.Group(session.Id).SendAsync("ScoreUpdate", ScoreDtoBuilder.Construire(session));
    }

    public async Task PauseGame()
    {
        var session = ResoudreSessionHost();
        if (session.EnPause) return;

        session.EnPause = true;
        session.PauseDemarreeA = DateTimeOffset.UtcNow;

        await Clients.Group(session.Id).SendAsync("GamePaused");
    }

    public async Task ResumeGame()
    {
        var session = ResoudreSessionHost();
        if (!session.EnPause || session.PauseDemarreeA is null) return;

        var pauseMs = (long)(DateTimeOffset.UtcNow - session.PauseDemarreeA.Value).TotalMilliseconds;

        var round = session.RoundCourant();
        if (round?.DebutRound is not null) round.DureeEnPauseMs += pauseMs;

        var bonusRound = session.SerieCourante().BonusRound;
        if (bonusRound is not null && (bonusRound.DebutPhaseMise is not null || bonusRound.DebutPhaseQuestion is not null))
            bonusRound.DureeEnPauseMs += pauseMs;

        session.EnPause = false;
        session.PauseDemarreeA = null;

        await Clients.Group(session.Id).SendAsync("GameResumed");
    }

    public async Task EndGame()
    {
        var session = ResoudreSessionHost();
        session.Etat = GameState.Termine;
        timerCoordinator.Annuler(session.Id);
        bonusTimerCoordinator.Annuler(session.Id);

        await Clients.Group(session.Id).SendAsync("GameEnded", ScoreDtoBuilder.Construire(session));
    }

    // ----- Méthodes joueur -----

    public async Task<JoinGameResultDto> JoinGame(string code, string nom, string playerId)
    {
        var session = sessionStore.Get(code);
        if (session is null) return new JoinGameResultDto(false, "Partie introuvable.", 0, null);

        var joueur = session.Players.FirstOrDefault(p => p.PlayerId == playerId);
        var estReconnexion = joueur is not null;

        if (joueur is null)
        {
            joueur = new Player { PlayerId = playerId, Nom = nom, ConnectionId = Context.ConnectionId, EstConnecte = true };
            session.Players.Add(joueur);
        }
        else
        {
            joueur.ConnectionId = Context.ConnectionId;
            joueur.EstConnecte = true;
        }

        sessionStore.AssocierConnexion(Context.ConnectionId, code);
        await Groups.AddToGroupAsync(Context.ConnectionId, code);

        if (estReconnexion)
            await Clients.OthersInGroup(code).SendAsync("PlayerReconnected", new PlayerConnectionChangedDto(joueur.PlayerId, true));
        else
            await Clients.OthersInGroup(code).SendAsync("PlayerJoined", new PlayerJoinedDto(joueur.PlayerId, joueur.Nom));

        return new JoinGameResultDto(true, null, joueur.Score, joueur.TeamId);
    }

    public async Task<RoundAnswerResultDto> SubmitAnswer(SubmitAnswerRequestDto request)
    {
        var session = ResoudreSession();
        var joueur = session.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId)
                     ?? throw new HubException("Joueur non reconnu dans cette partie.");

        var round = session.RoundCourant() ?? throw new HubException("Aucun round en cours.");
        var track = tracksRepository.GetById(round.TrackId) ?? throw new HubException("Morceau introuvable dans le catalogue.");
        var serie = session.SerieCourante();

        var reponse = roundService.SoumettreReponse(session, round, serie.Config, track, joueur.PlayerId, request.Reponse, DateTimeOffset.UtcNow);
        if (reponse is null)
            return new RoundAnswerResultDto(false, 0, joueur.Score);

        await Clients.Group(session.Id).SendAsync("ScoreUpdate", ScoreDtoBuilder.Construire(session));

        return new RoundAnswerResultDto(reponse.EstCorrecte, reponse.Points, joueur.Score);
    }

    public bool SelectStake(SelectStakeRequestDto request)
    {
        var session = ResoudreSession();
        var joueur = session.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId)
                     ?? throw new HubException("Joueur non reconnu dans cette partie.");

        var bonusRound = session.SerieCourante().BonusRound ?? throw new HubException("Aucune question bonus en cours.");

        return bonusRoundService.EnregistrerMise(bonusRound, joueur.PlayerId, request.PalierIndex);
    }

    public async Task<BonusAnswerResultDto> SubmitBonusAnswer(SubmitBonusAnswerRequestDto request)
    {
        var session = ResoudreSession();
        var joueur = session.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId)
                     ?? throw new HubException("Joueur non reconnu dans cette partie.");

        var serie = session.SerieCourante();
        var bonusRound = serie.BonusRound ?? throw new HubException("Aucune question bonus en cours.");
        var track = tracksRepository.GetById(bonusRound.TrackId) ?? throw new HubException("Morceau introuvable dans le catalogue.");

        var reponse = bonusRoundService.SoumettreReponse(session, bonusRound, serie.Config, track, joueur.PlayerId, request.Reponse, DateTimeOffset.UtcNow);
        if (reponse is null)
            return new BonusAnswerResultDto(false, 0, joueur.Score);

        await Clients.Group(session.Id).SendAsync("ScoreUpdate", ScoreDtoBuilder.Construire(session));

        return new BonusAnswerResultDto(reponse.EstCorrecte, reponse.Points, joueur.Score);
    }

    // ----- Cycle de connexion -----

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var code = sessionStore.ObtenirCodeParConnexion(Context.ConnectionId);
        sessionStore.DissocierConnexion(Context.ConnectionId);

        if (code is not null && sessionStore.Get(code) is { } session)
        {
            var joueur = session.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
            if (joueur is not null)
            {
                joueur.EstConnecte = false;
                await Clients.Group(code).SendAsync("PlayerDisconnected", new PlayerConnectionChangedDto(joueur.PlayerId, false));
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    // ----- Aides privées -----

    private GameSession ResoudreSession()
    {
        var code = sessionStore.ObtenirCodeParConnexion(Context.ConnectionId)
                   ?? throw new HubException("Aucune partie associée à cette connexion.");
        return sessionStore.Get(code) ?? throw new HubException("Partie introuvable.");
    }

    private GameSession ResoudreSessionHost()
    {
        var session = ResoudreSession();
        if (session.HostConnectionId != Context.ConnectionId)
            throw new HubException("Seul le host peut effectuer cette action.");
        return session;
    }

    private static long CalculerTempsEcouleMs(GameSession session, Round round)
    {
        var pauseEnCoursMs = session.EnPause && session.PauseDemarreeA is not null
            ? (DateTimeOffset.UtcNow - session.PauseDemarreeA.Value).TotalMilliseconds
            : 0;

        return (long)((DateTimeOffset.UtcNow - round.DebutRound!.Value).TotalMilliseconds - (round.DureeEnPauseMs + pauseEnCoursMs));
    }
}
