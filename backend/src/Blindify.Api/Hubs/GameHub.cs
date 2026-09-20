using System.Security.Cryptography;
using System.Text;
using Blindify.Api.Contracts;
using Blindify.Api.Jokers;
using Blindify.Application.Awards;
using Blindify.Application.Bonus;
using Blindify.Application.Rounds;
using Blindify.Application.Scoring;
using Blindify.Application.Sessions;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;
using Blindify.Domain.Enums;
using Blindify.Domain.Jokers;
using Blindify.Infrastructure.Flags;
using Blindify.Infrastructure.Stats;
using Blindify.Infrastructure.Tracks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;

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
    IScoringService scoringService,
    ITracksRepository tracksRepository,
    IStatsRepository statsRepository,
    IFlagsRepository flagsRepository,
    IJokerCoverTokenStore jokerCoverTokenStore,
    RoundTimerCoordinator timerCoordinator,
    BonusTimerCoordinator bonusTimerCoordinator,
    IConfiguration configuration) : Hub
{
    // ----- Méthodes host -----

    public async Task<CreateGameResultDto> CreateGame(CreateGameRequestDto request)
    {
        var teams = request.ModeEquipe
            ? (request.NomsEquipes ?? []).Select(nom => new Team { Id = Guid.NewGuid().ToString("N")[..8], Nom = nom }).ToList()
            : [];

        string code;
        do { code = codeGenerator.GenererCode(); } while (sessionStore.Exists(code));

        // Distinct du code de partie (public, connu de tous les joueurs) — voir GameSession.HostSecret.
        var hostSecret = Guid.NewGuid().ToString("N");

        var session = new GameSession
        {
            Id = code,
            Etat = GameState.Lobby,
            ModeEquipe = request.ModeEquipe,
            Config = new GameConfig(),
            SeriesList = [], // configuré séparément par ConfigurerPartie — voir CreateGameRequestDto
            Teams = teams,
            HostConnectionId = Context.ConnectionId,
            HostSecret = hostSecret
        };

        sessionStore.Add(session);
        sessionStore.AssocierConnexion(Context.ConnectionId, code);
        await Groups.AddToGroupAsync(Context.ConnectionId, code);

        return new CreateGameResultDto(code, teams.Select(t => new TeamDto(t.Id, t.Nom)).ToList(), hostSecret);
    }

    // Retour utilisateur (playtest 2026-08-24) : auparavant, toute la configuration du blindtest
    // (séries/tags/rounds) devait être soumise AVANT que le lobby n'existe — une erreur de config
    // forçait à recréer toute la partie, donc les joueurs à quitter et rouvrir l'app Flutter faute
    // d'un moyen de rejoindre une nouvelle partie sans redémarrage complet. Séparé de CreateGame :
    // rappelable autant de fois que nécessaire tant que la partie n'a pas démarré, remplace
    // entièrement la configuration précédente (recalcule la sélection de morceaux depuis zéro).
    public Task ConfigurerPartie(ConfigurerPartieRequestDto request)
    {
        var session = ResoudreSessionHost();
        lock (session.Lock)
        {
            if (session.Etat != GameState.Lobby)
                throw new HubException("La partie a déjà démarré — configuration verrouillée.");

            if (!scoringService.EstPenaliteAbsenceEquitable(request.PenaliteAbsenceReponse, request.PenaliteMauvaiseReponseRatio, request.PointsMin))
                throw new HubException(
                    $"Pénalité d'absence ({request.PenaliteAbsenceReponse}) trop sévère par rapport à la pénalité de mauvaise réponse et à PointsMin ({request.PointsMin}) : un joueur hésitant serait mathématiquement incité à ne jamais répondre.");

            // Résolu AVANT la construction des séries (et non après, comme avant V2) : SeriesPlanner.
            // PaliersPourSerie a besoin de Config.FacteurProgressionPaliers pour chaque série.
            session.Config = request.Config ?? session.Config;

            var tagsParSerie = SeriesPlanner.AssignerThemesAuxSeries(request.ThemesVivier, request.NombreSeries);
            var catalogue = tracksRepository.GetAll();
            // V2, section 12.4 : pré-remplir l'exclusion avec les morceaux bloqués plutôt que de changer
            // la signature de SelectionnerMorceaux — dejaUtilises EST déjà l'ensemble d'exclusion qu'il
            // accumule série après série.
            var dejaUtilises = session.Config.ExclureMorceauxSignales ? flagsRepository.ObtenirTrackIdsBloquants() : [];
            var seriesList = new List<Series>();

            for (var index = 0; index < request.NombreSeries; index++)
            {
                var tags = tagsParSerie[index];
                var morceaux = roundService.SelectionnerMorceaux(catalogue, tags, request.NombreRoundsClassiques, dejaUtilises, statsRepository.GetPlayCount);
                if (morceaux.Count < request.NombreRoundsClassiques)
                {
                    var themeLabel = tags.Count > 0 ? string.Join("/", tags) : "aléatoire";
                    throw new HubException(
                        $"Thème « {themeLabel} » : seulement {morceaux.Count} morceau(x) disponible(s) pour {request.NombreRoundsClassiques} rounds demandés (déjà utilisés par une autre série exclus). Réduis le nombre de rounds ou choisis un thème plus large.");
                }

                var roundModes = SeriesPlanner.PickRandomRoundModes(request.NombreRoundsClassiques);
                var rounds = morceaux.Select((track, i) => new Round { TrackId = track.Id, Mode = roundModes[i] }).ToList();
                var seriesConfig = new SeriesConfig
                {
                    NombreRoundsClassiques = request.NombreRoundsClassiques,
                    DureeFenetreReponseMs = request.DureeFenetreReponseMs,
                    PointsMax = request.PointsMax,
                    PointsMin = request.PointsMin,
                    PenaliteMauvaiseReponseRatio = request.PenaliteMauvaiseReponseRatio,
                    PenaliteAbsenceReponse = request.PenaliteAbsenceReponse,
                    PaliersDeMise = SeriesPlanner.PaliersPourSerie(index, session.Config.FacteurProgressionPaliers),
                    DureePhaseMiseMs = request.DureePhaseMiseMs,
                    DureePhaseQuestionMs = request.DureePhaseQuestionMs,
                };
                seriesList.Add(new Series { Index = index, Config = seriesConfig, Tags = tags, Rounds = rounds });
            }

            session.SeriesList = seriesList;
            session.SerieCouranteIndex = 0;
            session.RoundCourantIndex = -1;
        }

        return Task.CompletedTask;
    }

    public async Task<HostStateSnapshotDto> RejoinAsHost(string code, string hostSecret)
    {
        var session = sessionStore.Get(code) ?? throw new HubException("Partie introuvable.");
        if (session.HostSecret != hostSecret)
            throw new HubException("Secret host invalide.");

        lock (session.Lock) { session.HostConnectionId = Context.ConnectionId; }
        sessionStore.AssocierConnexion(Context.ConnectionId, code);
        await Groups.AddToGroupAsync(Context.ConnectionId, code);

        Round? round;
        long positionMs;
        Track? track;
        lock (session.Lock)
        {
            round = session.RoundCourant();
            if (round?.DebutRound is null)
                return new HostStateSnapshotDto(session.EnPause, null, null, null, null, null, null, null);

            track = tracksRepository.GetById(round.TrackId);
            positionMs = Math.Max(0, CalculerTempsEcouleMs(session, round.DebutRound.Value, round.DureeEnPauseMs));
        }

        return new HostStateSnapshotDto(
            session.EnPause,
            round.Mode,
            round.Cible,
            track?.Id,
            track?.FilePath,
            track?.RefrainStartMs,
            positionMs,
            session.SerieCourante().Config.DureeFenetreReponseMs);
    }

    public async Task StartRound()
    {
        var session = ResoudreSessionHost();
        Round round;
        Series serie;
        Track track;
        List<QcmOptionDto>? qcmOptions;
        List<string>? anneeOptions;

        lock (session.Lock)
        {
            if (session.SeriesList.Count == 0)
                throw new HubException("Aucune série configurée — configure le blindtest (ConfigurerPartie) avant de le démarrer.");

            if (session.RoundCourantIndex == -1) session.RoundCourantIndex = 0;

            serie = session.SerieCourante();
            if (session.RoundCourantIndex >= serie.Rounds.Count)
                throw new HubException("Plus de round classique à démarrer dans cette série.");

            round = serie.Rounds[session.RoundCourantIndex];
            track = tracksRepository.GetById(round.TrackId) ?? throw new HubException("Morceau introuvable dans le catalogue.");

            roundService.DemarrerRound(round, track, tracksRepository.GetAll(), serie.Tags, session.Config, DateTimeOffset.UtcNow);
            session.Etat = GameState.EnCours;
            statsRepository.IncrementPlayCount(track.Id);

            (qcmOptions, round.Options) = ConstruireQcmOptions(round.QcmOptionTrackIds, track, round.Cible, session.Config, tracksRepository);
            anneeOptions = round.AnneeOptions?.Select(a => a.ToString()).ToList();
        }

        if (session.HostConnectionId is not null)
        {
            await Clients.Client(session.HostConnectionId)
                .SendAsync("RoundStarted", new RoundStartedForHostDto(round.Mode, round.Cible, round.Id, track.Id, track.FilePath, track.RefrainStartMs, serie.Config.DureeFenetreReponseMs, qcmOptions, anneeOptions));
        }

        var joueursConnectes = session.Players.Where(p => p.ConnectionId is not null).Select(p => p.ConnectionId!).ToList();
        await Clients.Clients(joueursConnectes)
            .SendAsync("RoundStarted", new RoundStartedForPlayersDto(round.Mode, round.Cible, round.Id, serie.Config.DureeFenetreReponseMs, serie.Index, qcmOptions, TempsEcouleMs: 0, AnneeOptions: anneeOptions));

        timerCoordinator.DemarrerSurveillance(session.Id, serie.Config);
    }

    public async Task StartBonusRound()
    {
        var session = ResoudreSessionHost();
        Series serie;

        lock (session.Lock)
        {
            if (session.SeriesList.Count == 0)
                throw new HubException("Aucune série configurée — configure le blindtest (ConfigurerPartie) avant de le démarrer.");

            serie = session.SerieCourante();

            if (serie.BonusRound is not null)
                throw new HubException("La question bonus de cette série a déjà été démarrée.");

            var dejaUtilises = session.SeriesList
                .SelectMany(s => s.Rounds.Select(r => r.TrackId))
                .Concat(session.SeriesList.Where(s => s.BonusRound is not null).Select(s => s.BonusRound!.TrackId))
                .ToHashSet();
            if (session.Config.ExclureMorceauxSignales) dejaUtilises.UnionWith(flagsRepository.ObtenirTrackIdsBloquants());

            var morceaux = roundService.SelectionnerMorceaux(tracksRepository.GetAll(), serie.Tags, 1, dejaUtilises, statsRepository.GetPlayCount);
            if (morceaux.Count == 0)
                throw new HubException("Pas assez de morceaux disponibles pour la question bonus.");

            var bonusRound = bonusRoundService.CreerBonusRound(morceaux[0], tracksRepository.GetAll(), serie.Tags, session.Config);
            serie.BonusRound = bonusRound;
            bonusRoundService.DemarrerPhaseMise(bonusRound, DateTimeOffset.UtcNow);
            statsRepository.IncrementPlayCount(morceaux[0].Id);
        }

        await Clients.Group(session.Id).SendAsync("BonusStakeOptions", new BonusStakeOptionsDto(serie.Config.PaliersDeMise, serie.BonusRound!.Id, serie.Config.DureePhaseMiseMs, serie.Index, TempsEcouleMs: 0));

        bonusTimerCoordinator.DemarrerSurveillance(session.Id, serie.Config);
    }

    // Retour utilisateur (playtest 2026-08-24) : le host avait déjà un écran "Série B — Rock" avant
    // le premier round de chaque série, mais uniquement côté host/écran public (jamais diffusé aux
    // joueurs). Le host appelle cette méthode au même moment où il affichait déjà cet écran
    // localement (avant le premier round de la série, avant même StartRound) — le contenu (Tags)
    // vient maintenant du serveur plutôt que d'être recopié côté client, mais le déclenchement reste
    // un appel explicite du host pour préserver la pause volontaire avant le début du round (voir
    // host/app.js:afficherIntroSerie et son délai avant StartRound/StartBonusRound).
    public async Task AnnoncerSerieCourante()
    {
        var session = ResoudreSessionHost();
        if (session.SeriesList.Count == 0)
            throw new HubException("Aucune série configurée — configure le blindtest (ConfigurerPartie) avant de le démarrer.");

        var serie = session.SerieCourante();
        await Clients.Group(session.Id).SendAsync("SerieAnnoncee", new SerieAnnonceeDto(serie.Index, serie.Tags));
    }

    public Task NextRound()
    {
        var session = ResoudreSessionHost();
        lock (session.Lock)
        {
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
            else
            {
                // Dernière série épuisée — le host doit appeler StartBonusRound() puis EndGame(). On avance
                // quand même le pointeur hors limites pour que StartRound() refuse désormais de rejouer le
                // dernier round au lieu de le relancer silencieusement.
                session.RoundCourantIndex++;
            }
        }

        return Task.CompletedTask;
    }

    public async Task ShowLeaderboard()
    {
        var session = ResoudreSessionHostOuAdmin();
        await Clients.Group(session.Id).SendAsync("LeaderboardShown", ScoreDtoBuilder.Construire(session));
    }

    public async Task<RoundAnswerResultDto> ValidateAnswerManually(ValidateAnswerManuallyRequestDto request)
    {
        var session = ResoudreSessionHost();
        RoundAnswer resultat;
        Player joueur;

        lock (session.Lock)
        {
            var round = session.RoundCourant() ?? throw new HubException("Aucun round en cours.");
            var serie = session.SerieCourante();

            resultat = roundService.ValiderManuellement(session, round, serie.Config, request.PlayerId, request.EstCorrecte)
                       ?? throw new HubException("Aucune réponse enregistrée pour ce joueur sur ce round.");

            joueur = session.Players.First(p => p.PlayerId == request.PlayerId);
        }

        await Clients.Group(session.Id).SendAsync("ScoreUpdate", ScoreDtoBuilder.Construire(session));

        return new RoundAnswerResultDto(resultat.EstCorrecte, resultat.Points, joueur.Score);
    }

    /// <summary>Authentifie la connexion courante comme admin pour la partie à laquelle elle est déjà
    /// associée (JoinGame préalable) — un mot de passe partagé (Admin:RemoteControlPassword, jamais
    /// en dur, vide par défaut = désactivé), pensé pour être saisi une fois dans l'app Flutter
    /// (réglages) plutôt que de exiger un accès physique au host web pour pause/tableau général/fin
    /// de partie. Comparaison à temps constant — même principe que POST /api/admin/restart
    /// (Program.cs). N'accorde JAMAIS les privilèges host complets (StartRound/ConfigurerPartie
    /// restent exclusifs à ResoudreSessionHost) — voir GameSession.AdminConnectionIds.</summary>
    public Task<AdminAuthResultDto> AuthenticateAdmin(string password)
    {
        var motDePasseConfigure = configuration["Admin:RemoteControlPassword"];
        if (string.IsNullOrEmpty(motDePasseConfigure))
            return Task.FromResult(new AdminAuthResultDto(false, "Contrôle admin désactivé — aucun mot de passe configuré côté serveur."));

        var estValide = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(motDePasseConfigure), Encoding.UTF8.GetBytes(password ?? ""));
        if (!estValide)
            return Task.FromResult(new AdminAuthResultDto(false, "Mot de passe incorrect."));

        var session = ResoudreSession();
        lock (session.Lock) { session.AdminConnectionIds.Add(Context.ConnectionId); }
        return Task.FromResult(new AdminAuthResultDto(true, null));
    }

    public async Task PauseGame()
    {
        var session = ResoudreSessionHostOuAdmin();
        lock (session.Lock)
        {
            if (session.EnPause) return;

            session.EnPause = true;
            session.PauseDemarreeA = DateTimeOffset.UtcNow;
        }

        await Clients.Group(session.Id).SendAsync("GamePaused");
    }

    public async Task ResumeGame()
    {
        var session = ResoudreSessionHostOuAdmin();
        lock (session.Lock)
        {
            if (!session.EnPause || session.PauseDemarreeA is null) return;

            var pauseMs = (long)(DateTimeOffset.UtcNow - session.PauseDemarreeA.Value).TotalMilliseconds;

            var round = session.RoundCourant();
            if (round?.DebutRound is not null) round.DureeEnPauseMs += pauseMs;

            var bonusRound = session.SerieCourante().BonusRound;
            if (bonusRound is not null && (bonusRound.DebutPhaseMise is not null || bonusRound.DebutPhaseQuestion is not null))
                bonusRound.DureeEnPauseMs += pauseMs;

            session.EnPause = false;
            session.PauseDemarreeA = null;
        }

        await Clients.Group(session.Id).SendAsync("GameResumed");
    }

    public async Task EndGame()
    {
        var session = ResoudreSessionHostOuAdmin();
        lock (session.Lock) { session.Etat = GameState.Termine; }
        timerCoordinator.Annuler(session.Id);
        bonusTimerCoordinator.Annuler(session.Id);

        var titres = TitresService.CalculerTitres(session, tracksRepository.GetById)
            .Select(t => new TitreDto(t.Code, t.Libelle, t.Description, t.PlayerIds))
            .ToList();

        await Clients.Group(session.Id).SendAsync("GameEnded", new GameEndedDto(ScoreDtoBuilder.Construire(session), titres));
    }

    /// <summary>Signalement en direct (V2, section 12.4) — host ou admin authentifié uniquement,
    /// jamais les joueurs (ResoudreSessionHostOuAdmin), depuis l'écran de reveal (l'admin est aussi un
    /// joueur, afficher le morceau plus tôt lui donnerait la réponse — appliqué côté client). Vérifie
    /// que le morceau a bien été joué dans cette session (jamais un id arbitraire) avant d'écrire dans
    /// flags.json. Diffuse MorceauSignale au host et aux admins seulement, pour confirmation visuelle
    /// même en cas de doublon (l'appelant sait alors que ce n'est pas la première fois).</summary>
    public async Task<SignalementResultDto> SignalerMorceau(SignalementRequestDto request)
    {
        var session = ResoudreSessionHostOuAdmin();
        RoundCible cible;
        RoundMode mode;
        List<string> serieTags;

        lock (session.Lock)
        {
            Round? round = null;
            BonusRound? bonusRound = null;
            Series? serieTrouvee = null;

            foreach (var serie in session.SeriesList)
            {
                round = serie.Rounds.FirstOrDefault(r => r.TrackId == request.TrackId);
                if (round is not null) { serieTrouvee = serie; break; }

                if (serie.BonusRound?.TrackId == request.TrackId) { bonusRound = serie.BonusRound; serieTrouvee = serie; break; }
            }

            if (serieTrouvee is null)
                throw new HubException("Ce morceau n'a pas été joué dans cette partie.");

            cible = round?.Cible ?? bonusRound!.Cible;
            mode = round?.Mode ?? bonusRound!.Mode;
            serieTags = serieTrouvee.Tags;
        }

        var par = Context.ConnectionId == session.HostConnectionId ? "host" : "admin";
        var (flagId, dejaSignale) = flagsRepository.Ajouter(
            request.TrackId, request.Raison, request.Commentaire, par, session.Id, serieTags, cible, mode);

        var track = tracksRepository.GetById(request.TrackId);
        var cibles = session.AdminConnectionIds.ToList();
        if (session.HostConnectionId is not null) cibles.Add(session.HostConnectionId);
        await Clients.Clients(cibles).SendAsync("MorceauSignale", new MorceauSignaleDto(request.TrackId, track?.Title ?? request.TrackId, track?.Artist ?? "?", request.Raison));

        return new SignalementResultDto(flagId, dejaSignale);
    }

    /// <summary>Relance une partie terminée avec le même groupe (même code, mêmes joueurs) — mêmes
    /// configs/modes de série qu'à la création, mais nouvelle sélection de morceaux et scores remis
    /// à zéro. Évite aux joueurs de devoir retaper le code pour une nouvelle manche.</summary>
    public async Task RejouerPartie()
    {
        var session = ResoudreSessionHost();
        lock (session.Lock)
        {
            if (session.Etat != GameState.Termine)
                throw new HubException("La partie doit être terminée avant de pouvoir être relancée.");

            // Retour utilisateur : rejouer avec le même groupe reproduisait les séries EXACTEMENT
            // dans le même ordre (même thème par position) — seuls les morceaux changeaient. On
            // reconstruit le vivier d'origine (thèmes distincts toutes séries confondues, [] si
            // "aléatoire" partout) et on le retire au sort à nouveau via SeriesPlanner, comme au tout
            // premier ConfigurerPartie, plutôt que de réutiliser l'assignation figée précédente.
            var vivierOriginal = session.SeriesList.SelectMany(s => s.Tags).Distinct().ToList();
            var tagsParSerieRebattus = SeriesPlanner.AssignerThemesAuxSeries(vivierOriginal, session.SeriesList.Count);

            var catalogue = tracksRepository.GetAll();
            var dejaUtilises = session.Config.ExclureMorceauxSignales ? flagsRepository.ObtenirTrackIdsBloquants() : [];
            var nouvellesSeries = new List<Series>();

            for (var index = 0; index < session.SeriesList.Count; index++)
            {
                var serie = session.SeriesList[index];
                var tags = tagsParSerieRebattus[index];
                var modes = serie.Rounds.Select(r => r.Mode).ToList();
                var morceaux = roundService.SelectionnerMorceaux(catalogue, tags, modes.Count, dejaUtilises, statsRepository.GetPlayCount);
                if (morceaux.Count < modes.Count)
                    throw new HubException($"Pas assez de morceaux disponibles dans le catalogue pour relancer la série {index + 1}.");

                var rounds = morceaux.Select((track, i) => new Round { TrackId = track.Id, Mode = modes[i] }).ToList();
                nouvellesSeries.Add(new Series { Index = index, Config = serie.Config, Tags = tags, Rounds = rounds });
            }

            session.SeriesList = nouvellesSeries;
            session.SerieCouranteIndex = 0;
            session.RoundCourantIndex = -1;
            session.Etat = GameState.Lobby;
            session.EnPause = false;
            session.PauseDemarreeA = null;

            foreach (var player in session.Players)
            {
                player.Score = 0;
                player.JokerUtilise = false; // V2, section 12.7 — un joker par partie complète
            }
        }

        await Clients.Group(session.Id).SendAsync("GameRestarted");
    }

    // ----- Méthodes joueur -----

    public async Task<JoinGameResultDto> JoinGame(string code, string nom, string playerId)
    {
        var session = sessionStore.Get(code);
        if (session is null) return new JoinGameResultDto(false, "Partie introuvable.", 0, null, [], [], null);

        Player joueur;
        bool estReconnexion;
        List<TeamDto> teams;
        List<PlayerSummaryDto> joueurs;
        EtatCourantJoueurDto? etatCourant;

        lock (session.Lock)
        {
            var existant = session.Players.FirstOrDefault(p => p.PlayerId == playerId);
            estReconnexion = existant is not null;

            if (existant is null)
            {
                joueur = new Player { PlayerId = playerId, Nom = nom, ConnectionId = Context.ConnectionId, EstConnecte = true };
                session.Players.Add(joueur);
            }
            else
            {
                joueur = existant;
                ReassocierConnexion(joueur, Context.ConnectionId);
            }

            teams = session.Teams.Select(t => new TeamDto(t.Id, t.Nom)).ToList();
            joueurs = session.Players.Select(p => new PlayerSummaryDto(p.PlayerId, p.Nom, p.EstConnecte, p.TeamId)).ToList();
            // Retour utilisateur (playtest 2026-09-06) : un joueur qui rejoint en pleine partie
            // (reconnexion) atterrissait au lobby en attendant le prochain round, sans pouvoir
            // participer à celui déjà en cours — voir ConstruireEtatCourantJoueur.
            etatCourant = ConstruireEtatCourantJoueur(session, playerId);
        }

        // Posé aussi ici (pas seulement dans OnConnectedAsync) pour couvrir le tout premier join d'un
        // joueur — avant cet appel, aucune reconnexion automatique n'a encore eu l'occasion de le poser.
        Context.Items["playerId"] = playerId;
        sessionStore.AssocierConnexion(Context.ConnectionId, code);
        await Groups.AddToGroupAsync(Context.ConnectionId, code);

        if (estReconnexion)
            await Clients.OthersInGroup(code).SendAsync("PlayerReconnected", new PlayerConnectionChangedDto(joueur.PlayerId, true));
        else
            await Clients.OthersInGroup(code).SendAsync("PlayerJoined", new PlayerJoinedDto(joueur.PlayerId, joueur.Nom));

        return new JoinGameResultDto(true, null, joueur.Score, joueur.TeamId, teams, joueurs, etatCourant, !joueur.JokerUtilise);
    }

    /// <summary>Rejoint (ou change) d'équipe — autorisé à tout moment, pas seulement au lobby, pour
    /// rester tolérant à une reconnexion tardive ou un choix corrigé.</summary>
    public async Task JoinTeam(string teamId)
    {
        var session = ResoudreSession();
        string playerId;
        string teamIdRetenu;

        lock (session.Lock)
        {
            var joueur = ResoudrePlayer(session);

            var team = session.Teams.FirstOrDefault(t => t.Id == teamId)
                       ?? throw new HubException("Équipe introuvable.");

            joueur.TeamId = team.Id;
            playerId = joueur.PlayerId;
            teamIdRetenu = team.Id;
        }

        await Clients.Group(session.Id).SendAsync("PlayerTeamChanged", new PlayerTeamChangedDto(playerId, teamIdRetenu));
    }

    public async Task<RoundAnswerResultDto> SubmitAnswer(SubmitAnswerRequestDto request)
    {
        var session = ResoudreSession();
        RoundAnswer? reponse;
        Player joueur;
        int tempsEcouleMs;

        lock (session.Lock)
        {
            joueur = ResoudrePlayer(session);

            var round = session.RoundCourant() ?? throw new HubException("Aucun round en cours.");
            var track = tracksRepository.GetById(round.TrackId) ?? throw new HubException("Morceau introuvable dans le catalogue.");
            var serie = session.SerieCourante();

            reponse = roundService.SoumettreReponse(session, round, serie.Config, track, joueur.PlayerId, request.RoundId, request.Reponse, DateTimeOffset.UtcNow, tracksRepository.GetById);
            if (reponse is null)
                return new RoundAnswerResultDto(false, 0, joueur.Score);

            tempsEcouleMs = round.DebutRound is null ? 0 : (int)Math.Max(0, CalculerTempsEcouleMs(session, round.DebutRound.Value, round.DureeEnPauseMs));
        }

        // Retour utilisateur : réaction visuelle sur l'écran public dès qu'un joueur répond, avec un
        // classement de rapidité — voir PlayerAnsweredDto pour ce qui est volontairement omis.
        await Clients.Group(session.Id).SendAsync("PlayerAnswered", new PlayerAnsweredDto(joueur.PlayerId, tempsEcouleMs));
        await Clients.Group(session.Id).SendAsync("ScoreUpdate", ScoreDtoBuilder.Construire(session));

        return new RoundAnswerResultDto(reponse.EstCorrecte, reponse.Points, joueur.Score);
    }

    /// <summary>V2, section 12.7 — un joker par joueur et par partie complète, round classique
    /// uniquement (jamais en question bonus, pas de méthode équivalente côté BonusRoundService).
    /// Toutes les conditions de refus sont vérifiées dans RoundService.UtiliserJoker (pause, roundId
    /// périmé, déjà répondu, déjà utilisé) : null se traduit ici en HubException, ce contrat n'ayant
    /// pas de DTO de résultat avec indicateur d'échec comme SubmitAnswer.</summary>
    public async Task<JokerIndiceDto> UtiliserJoker(Guid roundId)
    {
        var session = ResoudreSession();
        JokerIndice? indice;
        Player joueur;
        Round round;
        Track track;

        lock (session.Lock)
        {
            joueur = ResoudrePlayer(session);
            round = session.RoundCourant() ?? throw new HubException("Aucun round en cours.");
            track = tracksRepository.GetById(round.TrackId) ?? throw new HubException("Morceau introuvable dans le catalogue.");
            indice = roundService.UtiliserJoker(session, round, roundId, track, joueur.PlayerId);
        }

        if (indice is null) throw new HubException("Joker indisponible pour ce round.");

        // Cover floutée uniquement pour TapeReponse + Titre/Auteur (voir table de l'artéfact) — jamais
        // pour Film (la cover donnerait la réponse) ni Annee. Le jeton est ajouté après coup : il
        // dépend d'un service HTTP, hors de portée de JokerService (pur, Blindify.Application).
        if (round.Mode == RoundMode.TapeReponse && round.Cible is RoundCible.Titre or RoundCible.Auteur)
            indice.CoverToken = jokerCoverTokenStore.Emettre(track.Id);

        await Clients.Group(session.Id).SendAsync("JokerUtilise", new JokerUtiliseDto(joueur.PlayerId));
        return ConstruireJokerIndiceDto(indice);
    }

    private static JokerIndiceDto ConstruireJokerIndiceDto(JokerIndice indice) => new(
        indice.OptionsRetirees, indice.TuilesRestantes, indice.Structure, indice.Decennie,
        indice.CoverToken is null ? null : $"/api/joker/cover/{indice.CoverToken}");

    public bool SelectStake(SelectStakeRequestDto request)
    {
        var session = ResoudreSession();
        lock (session.Lock)
        {
            var joueur = ResoudrePlayer(session);

            var bonusRound = session.SerieCourante().BonusRound ?? throw new HubException("Aucune question bonus en cours.");

            return bonusRoundService.EnregistrerMise(session, bonusRound, joueur.PlayerId, request.RoundId, request.PalierIndex);
        }
    }

    public async Task<BonusAnswerResultDto> SubmitBonusAnswer(SubmitBonusAnswerRequestDto request)
    {
        var session = ResoudreSession();
        BonusAnswer? reponse;
        Player joueur;
        int tempsEcouleMs;

        lock (session.Lock)
        {
            joueur = ResoudrePlayer(session);

            var serie = session.SerieCourante();
            var bonusRound = serie.BonusRound ?? throw new HubException("Aucune question bonus en cours.");
            var track = tracksRepository.GetById(bonusRound.TrackId) ?? throw new HubException("Morceau introuvable dans le catalogue.");

            reponse = bonusRoundService.SoumettreReponse(session, bonusRound, serie.Config, track, joueur.PlayerId, request.RoundId, request.Reponse, DateTimeOffset.UtcNow, tracksRepository.GetById);
            if (reponse is null)
                return new BonusAnswerResultDto(false, 0, joueur.Score);

            tempsEcouleMs = bonusRound.DebutPhaseQuestion is null
                ? 0
                : (int)Math.Max(0, CalculerTempsEcouleMs(session, bonusRound.DebutPhaseQuestion.Value, bonusRound.DureeEnPauseMs));
        }

        await Clients.Group(session.Id).SendAsync("PlayerAnswered", new PlayerAnsweredDto(joueur.PlayerId, tempsEcouleMs));
        await Clients.Group(session.Id).SendAsync("ScoreUpdate", ScoreDtoBuilder.Construire(session));

        return new BonusAnswerResultDto(reponse.EstCorrecte, reponse.Points, joueur.Score);
    }

    // ----- Cycle de connexion -----

    /// <summary>Reconnexion automatique (V2) : le client (joueur) ouvre le hub avec
    /// "/hubs/game?code=XXXX&amp;playerId=..." dès que le code de partie est connu — SignalR réutilise
    /// cette URL à chaque reconnexion transport (withAutomaticReconnect), donc ce handler s'exécute
    /// aussi bien à la connexion initiale (avant même le premier JoinGame, où il ne trouve encore aucun
    /// Player et ne fait rien) qu'à chaque reconnexion (où il retrouve le Player par playerId et le
    /// rattache SANS que le client ait besoin de rappeler JoinGame). Ne concerne jamais le host : celui-ci
    /// n'a pas de playerId et continue de passer par RejoinAsHost (authentifié par hostSecret), jamais par
    /// une simple query string non authentifiée.</summary>
    public override async Task OnConnectedAsync()
    {
        var query = Context.GetHttpContext()?.Request.Query;
        var code = query?["code"].ToString();
        var playerId = query?["playerId"].ToString();

        if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(playerId) && sessionStore.Get(code) is { } session)
        {
            Player? joueur;
            lock (session.Lock)
            {
                joueur = session.Players.FirstOrDefault(p => p.PlayerId == playerId);
                if (joueur is not null) ReassocierConnexion(joueur, Context.ConnectionId);
            }

            if (joueur is not null)
            {
                Context.Items["playerId"] = playerId;
                sessionStore.AssocierConnexion(Context.ConnectionId, code);
                await Groups.AddToGroupAsync(Context.ConnectionId, code);

                EtatCourantJoueurDto? etatCourant;
                lock (session.Lock) { etatCourant = ConstruireEtatCourantJoueur(session, playerId); }

                await Clients.Caller.SendAsync("EtatCourant", new EtatCourantConnexionDto(joueur.Score, joueur.TeamId, etatCourant, !joueur.JokerUtilise));
                await Clients.OthersInGroup(code).SendAsync("PlayerReconnected", new PlayerConnectionChangedDto(playerId, true));
            }
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;
        var code = sessionStore.ObtenirCodeParConnexion(connectionId);
        sessionStore.DissocierConnexion(connectionId);

        if (code is not null && sessionStore.Get(code) is { } session)
        {
            string? playerId;
            lock (session.Lock)
            {
                playerId = session.Players.FirstOrDefault(p => p.ConnectionId == connectionId)?.PlayerId;
                session.AdminConnectionIds.Remove(connectionId);
            }

            if (playerId is not null)
            {
                // Délai de grâce (V2, GameConfig.DelaiGraceDeconnexionMs) : une micro-coupure suivie
                // d'une reconnexion automatique (OnConnectedAsync ci-dessus) ne doit pas faire clignoter
                // l'indicateur de connexion côté host. On revérifie après coup plutôt que d'annuler une
                // tâche différée : plus simple, et cette méthode est déjà awaitable tant qu'elle veut.
                var delaiMs = session.Config.DelaiGraceDeconnexionMs;
                if (delaiMs > 0) await Task.Delay(delaiMs);

                bool toujoursDeconnecte;
                lock (session.Lock)
                {
                    var joueur = session.Players.FirstOrDefault(p => p.PlayerId == playerId);
                    toujoursDeconnecte = joueur is not null && joueur.ConnectionId == connectionId;
                    if (toujoursDeconnecte) joueur!.EstConnecte = false;
                }

                if (toujoursDeconnecte)
                    await Clients.Group(code).SendAsync("PlayerDisconnected", new PlayerConnectionChangedDto(playerId, false));
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    // ----- Aides privées -----

    private static void ReassocierConnexion(Player joueur, string connectionId)
    {
        joueur.ConnectionId = connectionId;
        joueur.EstConnecte = true;
    }

    /// <summary>Retrouve le joueur courant via l'identité posée par JoinGame/OnConnectedAsync
    /// (Context.Items["playerId"]) plutôt que par ConnectionId (V2) — un ConnectionId change à chaque
    /// reconnexion transport, alors que Context.Items est repeuplé à chaque connexion (voir
    /// OnConnectedAsync) donc reste fiable même juste après une reconnexion automatique, avant tout
    /// nouvel appel explicite à JoinGame. Appelé sous session.Lock par tous ses appelants.</summary>
    private Player ResoudrePlayer(GameSession session)
    {
        var playerId = Context.Items.TryGetValue("playerId", out var value) ? value as string : null;
        if (playerId is null)
            throw new HubException("Joueur non reconnu dans cette partie.");

        return session.Players.FirstOrDefault(p => p.PlayerId == playerId)
               ?? throw new HubException("Joueur non reconnu dans cette partie.");
    }

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

    /// <summary>Version élargie de ResoudreSessionHost — accepte aussi un admin authentifié (voir
    /// AuthenticateAdmin/GameSession.AdminConnectionIds). Réservée aux actions sans effet sur la
    /// lecture audio (pause/reprise/tableau général/fin de partie) ; tout le reste du cycle de jeu
    /// (StartRound, ConfigurerPartie, CreateGame...) reste exclusif au host via ResoudreSessionHost.</summary>
    private GameSession ResoudreSessionHostOuAdmin()
    {
        var session = ResoudreSession();
        if (session.HostConnectionId != Context.ConnectionId && !session.AdminConnectionIds.Contains(Context.ConnectionId))
            throw new HubException("Seul le host ou un admin authentifié peut effectuer cette action.");
        return session;
    }

    /// <summary>Feinte QCM purement visuelle (retour utilisateur) — voir GameConfig.ProbabiliteQcmFeinteChamp.
    /// Remplace le champ affiché d'un distracteur tiré au sort par le champ opposé du morceau
    /// correct, sans toucher à son TrackId : le sélectionner reste une mauvaise réponse normale.
    /// Retourne le TrackId de l'option modifiée (V2, pour RoundOption.EstFeinte), ou null si aucune
    /// feinte n'a été appliquée.</summary>
    // Interne plutôt que privé : réutilisé par BonusTimerCoordinator pour appliquer les mêmes
    // feintes QCM à la question bonus (retour utilisateur : QCM aussi disponible en bonus).
    internal static string? AppliquerFeinteEventuelle(List<QcmOptionDto> options, Track correct, RoundCible cible, GameConfig config)
    {
        // Pas de dualité "champ opposé" pertinente pour Film (pas de second champ à échanger).
        if (cible == RoundCible.Film) return null;
        if (Random.Shared.NextDouble() >= config.ProbabiliteQcmFeinteChamp) return null;

        var distracteurs = options.Where(o => o.TrackId != correct.Id).ToList();
        if (distracteurs.Count == 0) return null;

        var optionChoisie = distracteurs[Random.Shared.Next(distracteurs.Count)];
        var index = options.IndexOf(optionChoisie);

        options[index] = cible == RoundCible.Titre
            ? optionChoisie with { Title = correct.Artist }
            : optionChoisie with { Artist = correct.Title };
        return optionChoisie.TrackId;
    }

    /// <summary>Feinte texte inventé (retour utilisateur, ex. Bastille - Pompéi -> "Baptiste") —
    /// voir GameConfig.ProbabiliteQcmFeinteTexteArtiste. Contrairement à AppliquerFeinteEventuelle,
    /// le texte de substitution ne vient pas d'un champ réel du morceau correct mais de
    /// Track.TrapTextArtist, un leurre écrit à la main. Ne s'applique qu'en cible Auteur, et
    /// seulement si le morceau correct a un TrapTextArtist renseigné. Le TrackId du distracteur
    /// ne change pas : le sélectionner reste une mauvaise réponse normale. Retourne le TrackId
    /// modifié (V2) ou null — voir AppliquerFeinteEventuelle.</summary>
    internal static string? AppliquerFeinteTexteEventuelle(List<QcmOptionDto> options, Track correct, RoundCible cible, GameConfig config)
    {
        if (cible != RoundCible.Auteur || string.IsNullOrEmpty(correct.TrapTextArtist)) return null;
        if (Random.Shared.NextDouble() >= config.ProbabiliteQcmFeinteTexteArtiste) return null;

        var distracteurs = options.Where(o => o.TrackId != correct.Id).ToList();
        if (distracteurs.Count == 0) return null;

        var optionChoisie = distracteurs[Random.Shared.Next(distracteurs.Count)];
        var index = options.IndexOf(optionChoisie);

        options[index] = optionChoisie with { Artist = correct.TrapTextArtist };
        return optionChoisie.TrackId;
    }

    /// <summary>Factorisé depuis StartRound (round classique) et réutilisé par BonusTimerCoordinator
    /// (question bonus) — construit à la fois les DTOs envoyés au réseau (QcmOptions) et leur miroir
    /// persistable (Options, V2) à stocker une seule fois sur Round/BonusRound. Depuis V2, les
    /// feintes ne sont donc plus jamais recalculées pour un même round (voir
    /// QcmOptionsDepuisRoundOptions, utilisé par ConstruireEtatCourantJoueur à la reconnexion).</summary>
    internal static (List<QcmOptionDto>? QcmOptions, List<RoundOption>? Options) ConstruireQcmOptions(
        List<string>? qcmOptionTrackIds, Track correct, RoundCible cible, GameConfig config, ITracksRepository tracksRepository)
    {
        var qcmOptions = qcmOptionTrackIds?
            .Select(id => tracksRepository.GetById(id))
            .Where(t => t is not null)
            .Select(t => new QcmOptionDto(t!.Id, t.Title, t.Artist, FilmNameResolver.Resoudre(t)))
            .ToList();

        if (qcmOptions is null) return (null, null);

        var feinteTrackIds = new HashSet<string>();
        var feinteChamp = AppliquerFeinteEventuelle(qcmOptions, correct, cible, config);
        if (feinteChamp is not null) feinteTrackIds.Add(feinteChamp);
        var feinteTexte = AppliquerFeinteTexteEventuelle(qcmOptions, correct, cible, config);
        if (feinteTexte is not null) feinteTrackIds.Add(feinteTexte);

        var options = qcmOptions.Select(o => new RoundOption
        {
            TrackId = o.TrackId,
            TexteAffiche = TexteAffichePourCible(o, cible),
            EstFeinte = feinteTrackIds.Contains(o.TrackId),
            EstPiege = correct.TrapWith.Contains(o.TrackId),
        }).ToList();

        return (qcmOptions, options);
    }

    private static string TexteAffichePourCible(QcmOptionDto option, RoundCible cible) => cible switch
    {
        RoundCible.Titre => option.Title,
        RoundCible.Auteur => option.Artist,
        RoundCible.Film => option.Film,
        _ => option.Title,
    };

    /// <summary>Reconstruit les DTOs QCM envoyés aux clients à partir des options déjà persistées
    /// (V2, Round.Options/BonusRound.Options) — utilisé par ConstruireEtatCourantJoueur (reconnexion)
    /// plutôt que ConstruireQcmOptions, pour renvoyer EXACTEMENT les mêmes options (feintes comprises)
    /// que celles vues par les autres joueurs, au lieu de retirer une feinte au hasard à chaque
    /// reconnexion. Seul le champ correspondant à la cible provient du texte persisté (peut être une
    /// feinte) ; les deux autres sont résolus depuis le catalogue (jamais affichés au joueur, mais
    /// QcmOptionDto porte toujours les trois champs).</summary>
    private static List<QcmOptionDto>? QcmOptionsDepuisRoundOptions(List<RoundOption>? options, RoundCible cible, ITracksRepository tracksRepository)
    {
        if (options is null) return null;

        return options.Select(o =>
        {
            var track = tracksRepository.GetById(o.TrackId);
            var titre = cible == RoundCible.Titre ? o.TexteAffiche : track?.Title ?? o.TexteAffiche;
            var artiste = cible == RoundCible.Auteur ? o.TexteAffiche : track?.Artist ?? o.TexteAffiche;
            var film = cible == RoundCible.Film ? o.TexteAffiche : track is not null ? FilmNameResolver.Resoudre(track) : o.TexteAffiche;
            return new QcmOptionDto(o.TrackId, titre, artiste, film);
        }).ToList();
    }

    /// <summary>Reconstruit, pour un joueur qui (re)rejoint, la phase actuellement active (round
    /// classique ou question bonus) afin qu'il puisse répondre immédiatement plutôt que d'attendre
    /// le prochain événement serveur — voir EtatCourantJoueurDto. Appelé sous session.Lock (aucun
    /// accès concurrent aux entités mutables Round/BonusRound). Null si rien n'est activement en
    /// cours (lobby, partie terminée, ou phase déjà expirée mais pas encore clôturée par le timer
    /// coordinator correspondant — fenêtre de quelques centaines de ms, sans conséquence : le
    /// prochain événement (RoundEnded/BonusResult puis la suite) rattrape le client normalement).
    ///
    /// Depuis V2, les options QCM sont lues sur Round.Options/BonusRound.Options (déjà persistées à
    /// la construction du round, voir ConstruireQcmOptions/QcmOptionsDepuisRoundOptions) plutôt que
    /// recalculées ici — un joueur qui se reconnecte voit donc exactement les mêmes options (feintes
    /// comprises) que les autres, plutôt qu'un nouveau tirage de feinte à chaque reconnexion.</summary>
    private EtatCourantJoueurDto? ConstruireEtatCourantJoueur(GameSession session, string playerId)
    {
        if (session.Etat != GameState.EnCours) return null;

        var serie = session.SerieCourante();
        var bonusRound = serie.BonusRound;

        if (bonusRound is not null && bonusRound.DebutPhaseQuestion is not null)
        {
            var finAnticipee = bonusRound.EstCourse && bonusRound.Reponses.Count > 0;
            var ecouleMs = CalculerTempsEcouleMs(session, bonusRound.DebutPhaseQuestion.Value, bonusRound.DureeEnPauseMs);
            if (!finAnticipee && ecouleMs < serie.Config.DureePhaseQuestionMs)
            {
                var track = tracksRepository.GetById(bonusRound.TrackId);
                if (track is not null)
                {
                    var qcmOptions = QcmOptionsDepuisRoundOptions(bonusRound.Options, bonusRound.Cible, tracksRepository);
                    var anneeOptions = bonusRound.AnneeOptions?.Select(a => a.ToString()).ToList();
                    var dejaRepondu = bonusRound.Reponses.Any(r => r.PlayerId == playerId);
                    var dto = new BonusQuestionStartedForPlayersDto(bonusRound.Id, serie.Config.DureePhaseQuestionMs, bonusRound.Cible, serie.Index, bonusRound.Mode, qcmOptions, bonusRound.EstCourse, (int)Math.Max(0, ecouleMs), anneeOptions);
                    return new EtatCourantJoueurDto(PhaseJoueur.BonusQuestion, session.EnPause, dejaRepondu, null, null, dto);
                }
            }
        }
        else if (bonusRound is not null && bonusRound.DebutPhaseMise is not null)
        {
            var ecouleMs = CalculerTempsEcouleMs(session, bonusRound.DebutPhaseMise.Value, bonusRound.DureeEnPauseMs);
            if (ecouleMs < serie.Config.DureePhaseMiseMs)
            {
                var dejaMise = bonusRound.Mises.Any(m => m.PlayerId == playerId);
                var dto = new BonusStakeOptionsDto(serie.Config.PaliersDeMise, bonusRound.Id, serie.Config.DureePhaseMiseMs, serie.Index, (int)Math.Max(0, ecouleMs));
                return new EtatCourantJoueurDto(PhaseJoueur.BonusMise, session.EnPause, dejaMise, null, dto, null);
            }
        }

        var round = session.RoundCourant();
        if (round?.DebutRound is not null)
        {
            var ecouleMs = CalculerTempsEcouleMs(session, round.DebutRound.Value, round.DureeEnPauseMs);
            if (ecouleMs < serie.Config.DureeFenetreReponseMs)
            {
                var track = tracksRepository.GetById(round.TrackId);
                if (track is not null)
                {
                    var qcmOptions = QcmOptionsDepuisRoundOptions(round.Options, round.Cible, tracksRepository);
                    var anneeOptions = round.AnneeOptions?.Select(a => a.ToString()).ToList();
                    var dejaRepondu = round.Reponses.Any(r => r.PlayerId == playerId);
                    // V2, section 12.7 : rejoue le MÊME indice si ce joueur avait déjà utilisé son
                    // joker sur ce round avant la coupure — jamais un nouveau tirage à la reconnexion.
                    var jokerIndice = round.JokerIndicesParJoueur.TryGetValue(playerId, out var indiceExistant)
                        ? ConstruireJokerIndiceDto(indiceExistant)
                        : null;
                    var dto = new RoundStartedForPlayersDto(round.Mode, round.Cible, round.Id, serie.Config.DureeFenetreReponseMs, serie.Index, qcmOptions, (int)Math.Max(0, ecouleMs), anneeOptions, jokerIndice);
                    return new EtatCourantJoueurDto(PhaseJoueur.RoundClassique, session.EnPause, dejaRepondu, dto, null, null);
                }
            }
        }

        return null;
    }

    /// <summary>Généralisé sur (debut, dureeEnPauseMs) plutôt que spécifique à Round — réutilisé pour
    /// un BonusRound (DebutPhaseMise/DebutPhaseQuestion) par ConstruireEtatCourantJoueur.</summary>
    private static long CalculerTempsEcouleMs(GameSession session, DateTimeOffset debut, long dureeEnPauseMs)
    {
        var pauseEnCoursMs = session.EnPause && session.PauseDemarreeA is not null
            ? (DateTimeOffset.UtcNow - session.PauseDemarreeA.Value).TotalMilliseconds
            : 0;

        return (long)((DateTimeOffset.UtcNow - debut).TotalMilliseconds - (dureeEnPauseMs + pauseEnCoursMs));
    }
}
