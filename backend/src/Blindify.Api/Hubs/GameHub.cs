using Blindify.Api.Contracts;
using Blindify.Application.Bonus;
using Blindify.Application.Rounds;
using Blindify.Application.Sessions;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;
using Blindify.Domain.Enums;
using Blindify.Infrastructure.Stats;
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
    IStatsRepository statsRepository,
    RoundTimerCoordinator timerCoordinator,
    BonusTimerCoordinator bonusTimerCoordinator) : Hub
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

            var tagsParSerie = SeriesPlanner.AssignerThemesAuxSeries(request.ThemesVivier, request.NombreSeries);
            var catalogue = tracksRepository.GetAll();
            var dejaUtilises = new HashSet<string>();
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
                    PaliersDeMise = SeriesPlanner.PaliersPourSerie(index, request.NombreSeries),
                    DureePhaseMiseMs = request.DureePhaseMiseMs,
                    DureePhaseQuestionMs = request.DureePhaseQuestionMs,
                };
                seriesList.Add(new Series { Index = index, Config = seriesConfig, Tags = tags, Rounds = rounds });
            }

            session.SeriesList = seriesList;
            session.Config = request.Config ?? session.Config;
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

            qcmOptions = ConstruireQcmOptions(round.QcmOptionTrackIds, track, round.Cible, session.Config, tracksRepository);
        }

        if (session.HostConnectionId is not null)
        {
            await Clients.Client(session.HostConnectionId)
                .SendAsync("RoundStarted", new RoundStartedForHostDto(round.Mode, round.Cible, track.Id, track.FilePath, track.RefrainStartMs, serie.Config.DureeFenetreReponseMs, qcmOptions));
        }

        var joueursConnectes = session.Players.Where(p => p.ConnectionId is not null).Select(p => p.ConnectionId!).ToList();
        await Clients.Clients(joueursConnectes)
            .SendAsync("RoundStarted", new RoundStartedForPlayersDto(round.Mode, round.Cible, serie.Config.DureeFenetreReponseMs, serie.Index, qcmOptions, TempsEcouleMs: 0));

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

            var morceaux = roundService.SelectionnerMorceaux(tracksRepository.GetAll(), serie.Tags, 1, dejaUtilises, statsRepository.GetPlayCount);
            if (morceaux.Count == 0)
                throw new HubException("Pas assez de morceaux disponibles pour la question bonus.");

            var bonusRound = bonusRoundService.CreerBonusRound(morceaux[0], tracksRepository.GetAll(), serie.Tags, session.Config);
            serie.BonusRound = bonusRound;
            bonusRoundService.DemarrerPhaseMise(bonusRound, DateTimeOffset.UtcNow);
            statsRepository.IncrementPlayCount(morceaux[0].Id);
        }

        await Clients.Group(session.Id).SendAsync("BonusStakeOptions", new BonusStakeOptionsDto(serie.Config.PaliersDeMise, serie.Config.DureePhaseMiseMs, serie.Index, TempsEcouleMs: 0));

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
        var session = ResoudreSessionHost();
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

    public async Task PauseGame()
    {
        var session = ResoudreSessionHost();
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
        var session = ResoudreSessionHost();
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
        var session = ResoudreSessionHost();
        lock (session.Lock) { session.Etat = GameState.Termine; }
        timerCoordinator.Annuler(session.Id);
        bonusTimerCoordinator.Annuler(session.Id);

        await Clients.Group(session.Id).SendAsync("GameEnded", ScoreDtoBuilder.Construire(session));
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

            var catalogue = tracksRepository.GetAll();
            var dejaUtilises = new HashSet<string>();
            var nouvellesSeries = new List<Series>();

            foreach (var serie in session.SeriesList)
            {
                var modes = serie.Rounds.Select(r => r.Mode).ToList();
                var morceaux = roundService.SelectionnerMorceaux(catalogue, serie.Tags, modes.Count, dejaUtilises, statsRepository.GetPlayCount);
                if (morceaux.Count < modes.Count)
                    throw new HubException("Pas assez de morceaux disponibles dans le catalogue pour relancer cette série.");

                var rounds = morceaux.Select((track, i) => new Round { TrackId = track.Id, Mode = modes[i] }).ToList();
                nouvellesSeries.Add(new Series { Index = nouvellesSeries.Count, Config = serie.Config, Tags = serie.Tags, Rounds = rounds });
            }

            session.SeriesList = nouvellesSeries;
            session.SerieCouranteIndex = 0;
            session.RoundCourantIndex = -1;
            session.Etat = GameState.Lobby;
            session.EnPause = false;
            session.PauseDemarreeA = null;

            foreach (var player in session.Players) player.Score = 0;
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
                joueur.ConnectionId = Context.ConnectionId;
                joueur.EstConnecte = true;
            }

            teams = session.Teams.Select(t => new TeamDto(t.Id, t.Nom)).ToList();
            joueurs = session.Players.Select(p => new PlayerSummaryDto(p.PlayerId, p.Nom, p.EstConnecte, p.TeamId)).ToList();
            // Retour utilisateur (playtest 2026-09-06) : un joueur qui rejoint en pleine partie
            // (reconnexion) atterrissait au lobby en attendant le prochain round, sans pouvoir
            // participer à celui déjà en cours — voir ConstruireEtatCourantJoueur.
            etatCourant = ConstruireEtatCourantJoueur(session, playerId);
        }

        sessionStore.AssocierConnexion(Context.ConnectionId, code);
        await Groups.AddToGroupAsync(Context.ConnectionId, code);

        if (estReconnexion)
            await Clients.OthersInGroup(code).SendAsync("PlayerReconnected", new PlayerConnectionChangedDto(joueur.PlayerId, true));
        else
            await Clients.OthersInGroup(code).SendAsync("PlayerJoined", new PlayerJoinedDto(joueur.PlayerId, joueur.Nom));

        return new JoinGameResultDto(true, null, joueur.Score, joueur.TeamId, teams, joueurs, etatCourant);
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
            var joueur = session.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId)
                         ?? throw new HubException("Joueur non reconnu dans cette partie.");

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

        lock (session.Lock)
        {
            joueur = session.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId)
                     ?? throw new HubException("Joueur non reconnu dans cette partie.");

            var round = session.RoundCourant() ?? throw new HubException("Aucun round en cours.");
            var track = tracksRepository.GetById(round.TrackId) ?? throw new HubException("Morceau introuvable dans le catalogue.");
            var serie = session.SerieCourante();

            reponse = roundService.SoumettreReponse(session, round, serie.Config, track, joueur.PlayerId, request.Reponse, DateTimeOffset.UtcNow, tracksRepository.GetById);
            if (reponse is null)
                return new RoundAnswerResultDto(false, 0, joueur.Score);
        }

        await Clients.Group(session.Id).SendAsync("ScoreUpdate", ScoreDtoBuilder.Construire(session));

        return new RoundAnswerResultDto(reponse.EstCorrecte, reponse.Points, joueur.Score);
    }

    public bool SelectStake(SelectStakeRequestDto request)
    {
        var session = ResoudreSession();
        lock (session.Lock)
        {
            var joueur = session.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId)
                         ?? throw new HubException("Joueur non reconnu dans cette partie.");

            var bonusRound = session.SerieCourante().BonusRound ?? throw new HubException("Aucune question bonus en cours.");

            return bonusRoundService.EnregistrerMise(session, bonusRound, joueur.PlayerId, request.PalierIndex);
        }
    }

    public async Task<BonusAnswerResultDto> SubmitBonusAnswer(SubmitBonusAnswerRequestDto request)
    {
        var session = ResoudreSession();
        BonusAnswer? reponse;
        Player joueur;

        lock (session.Lock)
        {
            joueur = session.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId)
                     ?? throw new HubException("Joueur non reconnu dans cette partie.");

            var serie = session.SerieCourante();
            var bonusRound = serie.BonusRound ?? throw new HubException("Aucune question bonus en cours.");
            var track = tracksRepository.GetById(bonusRound.TrackId) ?? throw new HubException("Morceau introuvable dans le catalogue.");

            reponse = bonusRoundService.SoumettreReponse(session, bonusRound, serie.Config, track, joueur.PlayerId, request.Reponse, DateTimeOffset.UtcNow, tracksRepository.GetById);
            if (reponse is null)
                return new BonusAnswerResultDto(false, 0, joueur.Score);
        }

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
            string? playerId = null;
            lock (session.Lock)
            {
                var joueur = session.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
                if (joueur is not null)
                {
                    joueur.EstConnecte = false;
                    playerId = joueur.PlayerId;
                }
            }

            if (playerId is not null)
                await Clients.Group(code).SendAsync("PlayerDisconnected", new PlayerConnectionChangedDto(playerId, false));
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

    /// <summary>Feinte QCM purement visuelle (retour utilisateur) — voir GameConfig.ProbabiliteQcmFeinteChamp.
    /// Remplace le champ affiché d'un distracteur tiré au sort par le champ opposé du morceau
    /// correct, sans toucher à son TrackId : le sélectionner reste une mauvaise réponse normale.</summary>
    // Interne plutôt que privé : réutilisé par BonusTimerCoordinator pour appliquer les mêmes
    // feintes QCM à la question bonus (retour utilisateur : QCM aussi disponible en bonus).
    internal static void AppliquerFeinteEventuelle(List<QcmOptionDto> options, Track correct, RoundCible cible, GameConfig config)
    {
        // Pas de dualité "champ opposé" pertinente pour Film (pas de second champ à échanger).
        if (cible == RoundCible.Film) return;
        if (Random.Shared.NextDouble() >= config.ProbabiliteQcmFeinteChamp) return;

        var distracteurs = options.Where(o => o.TrackId != correct.Id).ToList();
        if (distracteurs.Count == 0) return;

        var optionChoisie = distracteurs[Random.Shared.Next(distracteurs.Count)];
        var index = options.IndexOf(optionChoisie);

        options[index] = cible == RoundCible.Titre
            ? optionChoisie with { Title = correct.Artist }
            : optionChoisie with { Artist = correct.Title };
    }

    /// <summary>Feinte texte inventé (retour utilisateur, ex. Bastille - Pompéi -> "Baptiste") —
    /// voir GameConfig.ProbabiliteQcmFeinteTexteArtiste. Contrairement à AppliquerFeinteEventuelle,
    /// le texte de substitution ne vient pas d'un champ réel du morceau correct mais de
    /// Track.TrapTextArtist, un leurre écrit à la main. Ne s'applique qu'en cible Auteur, et
    /// seulement si le morceau correct a un TrapTextArtist renseigné. Le TrackId du distracteur
    /// ne change pas : le sélectionner reste une mauvaise réponse normale.</summary>
    internal static void AppliquerFeinteTexteEventuelle(List<QcmOptionDto> options, Track correct, RoundCible cible, GameConfig config)
    {
        if (cible != RoundCible.Auteur || string.IsNullOrEmpty(correct.TrapTextArtist)) return;
        if (Random.Shared.NextDouble() >= config.ProbabiliteQcmFeinteTexteArtiste) return;

        var distracteurs = options.Where(o => o.TrackId != correct.Id).ToList();
        if (distracteurs.Count == 0) return;

        var optionChoisie = distracteurs[Random.Shared.Next(distracteurs.Count)];
        var index = options.IndexOf(optionChoisie);

        options[index] = optionChoisie with { Artist = correct.TrapTextArtist };
    }

    /// <summary>Factorisé depuis StartRound (round classique) et réutilisé par BonusTimerCoordinator
    /// (question bonus) ET ConstruireEtatCourantJoueur (resynchronisation à la reconnexion) — les
    /// trois doivent produire exactement les mêmes options pour un même round, seules les feintes
    /// (probabilistes, non persistées) peuvent varier d'un appel à l'autre si celui-ci est refait
    /// plus tard pour le même round ; sans conséquence, voir ConstruireEtatCourantJoueur.</summary>
    internal static List<QcmOptionDto>? ConstruireQcmOptions(List<string>? qcmOptionTrackIds, Track correct, RoundCible cible, GameConfig config, ITracksRepository tracksRepository)
    {
        var qcmOptions = qcmOptionTrackIds?
            .Select(id => tracksRepository.GetById(id))
            .Where(t => t is not null)
            .Select(t => new QcmOptionDto(t!.Id, t.Title, t.Artist, FilmNameResolver.Resoudre(t)))
            .ToList();

        if (qcmOptions is not null)
        {
            AppliquerFeinteEventuelle(qcmOptions, correct, cible, config);
            AppliquerFeinteTexteEventuelle(qcmOptions, correct, cible, config);
        }

        return qcmOptions;
    }

    /// <summary>Reconstruit, pour un joueur qui (re)rejoint, la phase actuellement active (round
    /// classique ou question bonus) afin qu'il puisse répondre immédiatement plutôt que d'attendre
    /// le prochain événement serveur — voir EtatCourantJoueurDto. Appelé sous session.Lock (aucun
    /// accès concurrent aux entités mutables Round/BonusRound). Null si rien n'est activement en
    /// cours (lobby, partie terminée, ou phase déjà expirée mais pas encore clôturée par le timer
    /// coordinator correspondant — fenêtre de quelques centaines de ms, sans conséquence : le
    /// prochain événement (RoundEnded/BonusResult puis la suite) rattrape le client normalement).
    ///
    /// Les feintes QCM (AppliquerFeinteEventuelle/AppliquerFeinteTexteEventuelle) sont probabilistes
    /// et non persistées sur le Round/BonusRound — reconstruire les options ici peut donc tirer une
    /// feinte différente de celle vue par les autres joueurs pour le même round. Sans conséquence :
    /// la validation de réponse compare du texte, jamais un TrackId (voir SubmitAnswerRequestDto),
    /// et personne ne compare les QCM entre téléphones — persister les options déjà construites sur
    /// l'entité serait plus rigoureux mais alourdirait le schéma pour un bénéfice invisible en jeu.</summary>
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
                    var qcmOptions = ConstruireQcmOptions(bonusRound.QcmOptionTrackIds, track, bonusRound.Cible, session.Config, tracksRepository);
                    var dejaRepondu = bonusRound.Reponses.Any(r => r.PlayerId == playerId);
                    var dto = new BonusQuestionStartedForPlayersDto(serie.Config.DureePhaseQuestionMs, bonusRound.Cible, serie.Index, bonusRound.Mode, qcmOptions, bonusRound.EstCourse, (int)Math.Max(0, ecouleMs));
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
                var dto = new BonusStakeOptionsDto(serie.Config.PaliersDeMise, serie.Config.DureePhaseMiseMs, serie.Index, (int)Math.Max(0, ecouleMs));
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
                    var qcmOptions = ConstruireQcmOptions(round.QcmOptionTrackIds, track, round.Cible, session.Config, tracksRepository);
                    var dejaRepondu = round.Reponses.Any(r => r.PlayerId == playerId);
                    var dto = new RoundStartedForPlayersDto(round.Mode, round.Cible, serie.Config.DureeFenetreReponseMs, serie.Index, qcmOptions, (int)Math.Max(0, ecouleMs));
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
