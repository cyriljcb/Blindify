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
using Blindify.Infrastructure.Presets;
using Blindify.Infrastructure.Stats;
using Blindify.Infrastructure.Tracks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;

namespace Blindify.Api.Hubs;

/// <summary>
/// Hub SignalR — contrat complet dans architecture.md section 10 : lobby, cycle de vie du round classique,
/// question bonus (mise + question ralentie), pause, reconnexion, tableau général, override manuel.
/// </summary>
public class GameHub(
    IGameSessionStore sessionStore,
    IGameCodeGenerator codeGenerator,
    IRoundService roundService,
    IBonusRoundService bonusRoundService,
    ITracksRepository tracksRepository,
    IStatsRepository statsRepository,
    IFlagsRepository flagsRepository,
    IPresetsRepository presetsRepository,
    IJokerCoverTokenStore jokerCoverTokenStore,
    RoundTimerCoordinator timerCoordinator,
    BonusTimerCoordinator bonusTimerCoordinator,
    IConfiguration configuration,
    ILogger<GameHub> logger) : Hub
{
    private const int LongueurMaxNomJoueur = 24;
    private const int LongueurMaxPlayerId = 64;
    private const int LongueurMaxReponse = 200;
    private const int LongueurMaxCommentaire = 500;
    private const int TentativesAdminMax = 5;

    // ----- Méthodes host -----

    public async Task<CreateGameResultDto> CreateGame(CreateGameRequestDto request)
    {
        var nomsEquipes = (request.NomsEquipes ?? [])
            .Select(n => (n ?? "").Trim())
            .Where(n => n.Length > 0)
            .Select(n => n.Length > LongueurMaxNomJoueur ? n[..LongueurMaxNomJoueur] : n)
            .ToList();
        var teams = request.ModeEquipe
            ? nomsEquipes.Select(nom => new Team { Id = Guid.NewGuid().ToString("N")[..8], Nom = nom }).ToList()
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

        await QuitterSessionPrecedenteAsync(code);
        sessionStore.Add(session);
        sessionStore.AssocierConnexion(Context.ConnectionId, code);
        await Groups.AddToGroupAsync(Context.ConnectionId, code);

        return new CreateGameResultDto(code, teams.Select(t => new TeamDto(t.Id, t.Nom)).ToList(), hostSecret);
    }

    // Retour utilisateur (playtest 2026-08-24) : configuration séparée de CreateGame, rappelable autant
    // de fois que nécessaire tant que la partie n'a pas démarré — remplace entièrement la configuration
    // précédente (recalcule la sélection de morceaux depuis zéro).
    public async Task ConfigurerPartie(ConfigurerPartieRequestDto request)
    {
        var session = ResoudreSessionHost();
        VotesThemesDto? voteFerme = null;
        lock (session.Lock)
        {
            if (session.Etat != GameState.Lobby)
                throw new HubException("La partie a déjà démarré — configuration verrouillée.");

            // Résolu AVANT la construction des séries : SeriesPlanner.PaliersPourSerie a besoin de
            // Config.FacteurProgressionPaliers / PaliersMiseBase pour chaque série. Validé avant d'être
            // appliqué (audit 2026-11-10) : une configuration refusée laisse la précédente intacte.
            var config = request.Config ?? session.Config;
            var erreur = ConfigurationPartieValidator.Valider(
                config, request.NombreSeries, request.NombreRoundsClassiques, request.DureeFenetreReponseMs,
                request.PointsMax, request.PointsMin, request.PenaliteMauvaiseReponseRatio, request.PenaliteAbsenceReponse,
                request.DureePhaseMiseMs, request.DureePhaseQuestionMs);
            if (erreur is not null) throw new HubException(erreur);

            // Retour utilisateur (2026-10-17) — thèmes tirés parmi les votes des joueurs plutôt que dans le
            // vivier coché par le host. Jamais plus de séries que de thèmes votés (refusé ici).
            List<List<string>> tagsParSerie;
            if (request.UtiliserVotes)
            {
                var vote = session.VoteThemes ?? throw new HubException("Aucun vote n'a été ouvert pour ce salon.");
                try
                {
                    tagsParSerie = SeriesPlanner.ChoisirThemesDepuisVotes(vote.VotesParPlayerId, request.NombreSeries)
                        .Select(theme => new List<string> { theme })
                        .ToList();
                }
                catch (ArgumentException ex)
                {
                    throw new HubException($"{ex.Message} Réduis le nombre de séries ou attends d'autres votes.");
                }
            }
            else
            {
                tagsParSerie = SeriesPlanner.AssignerThemesAuxSeries(request.ThemesVivier ?? [], request.NombreSeries);
            }
            var catalogue = tracksRepository.GetAll();
            // V2, section 12.4 : pré-remplir l'exclusion avec les morceaux bloqués — dejaUtilises EST déjà
            // l'ensemble d'exclusion qu'il accumule série après série.
            var dejaUtilises = config.ExclureMorceauxSignales ? flagsRepository.ObtenirTrackIdsBloquants() : [];
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
                    PaliersDeMise = SeriesPlanner.PaliersPourSerie(index, config.FacteurProgressionPaliers, config.PaliersMiseBase),
                    DureePhaseMiseMs = request.DureePhaseMiseMs,
                    DureePhaseQuestionMs = request.DureePhaseQuestionMs,
                };
                seriesList.Add(new Series { Index = index, Config = seriesConfig, Tags = tags, Rounds = rounds });
            }

            session.Config = config;
            session.Config.AfficherEcart = request.AfficherEcart;
            session.SeriesList = seriesList;
            session.SerieCouranteIndex = 0;
            session.RoundCourantIndex = -1;

            // Fermé seulement une fois la configuration réussie : un échec (thème trop maigre) laisse le
            // vote ouvert pour un nouvel essai.
            if (request.UtiliserVotes && session.VoteThemes is { } voteUtilise)
            {
                voteUtilise.Ouvert = false;
                voteUtilise.ThemesRetenus = tagsParSerie.Select(t => t[0]).ToList();
                voteFerme = ConstruireVotesThemes(voteUtilise);
            }
            else if (!request.UtiliserVotes && session.VoteThemes is { ThemesRetenus: not null } voteIgnore)
            {
                // Reconfiguration sans les votes : l'ancien tirage ne décrit plus la partie.
                voteIgnore.ThemesRetenus = null;
                voteFerme = ConstruireVotesThemes(voteIgnore);
            }
        }

        if (voteFerme is not null)
            await Clients.Group(session.Id).SendAsync("VotesThemesMisAJour", voteFerme);
    }

    /// <summary>Retour utilisateur (2026-10-17) — ouvre (ou rouvre) au lobby le vote des thèmes par les
    /// joueurs : chacun choisit jusqu'à VotesParJoueur tags du catalogue (ceux ayant au moins
    /// NombreRoundsMin morceaux), puis ConfigurerPartie(UtiliserVotes) tire les thèmes joués. Une
    /// réouverture conserve les votes déjà exprimés encore valides.</summary>
    public async Task OuvrirVoteThemes(OuvrirVoteThemesRequestDto request)
    {
        var session = ResoudreSessionHost();
        if (request.VotesParJoueur < 1)
            throw new HubException("Il faut au moins un vote par joueur.");

        var themes = tracksRepository.GetAll()
            .SelectMany(t => t.Tags.Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= request.NombreRoundsMin)
            .Select(g => g.Key)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        VotesThemesDto decompte;
        lock (session.Lock)
        {
            if (session.Etat != GameState.Lobby)
                throw new HubException("Le vote n'est possible qu'avant le début de la partie.");

            var vote = session.VoteThemes ??= new VoteThemes();
            vote.Ouvert = true;
            vote.ThemesRetenus = null;
            vote.VotesParJoueur = request.VotesParJoueur;
            vote.ThemesProposes = themes;
            foreach (var playerId in vote.VotesParPlayerId.Keys.ToList())
            {
                var conserves = vote.VotesParPlayerId[playerId].Where(themes.Contains).Take(request.VotesParJoueur).ToList();
                if (conserves.Count > 0) vote.VotesParPlayerId[playerId] = conserves;
                else vote.VotesParPlayerId.Remove(playerId);
            }
            decompte = ConstruireVotesThemes(vote);
        }

        await Clients.Group(session.Id).SendAsync("VoteThemesOuvert", new VoteThemesOuvertDto(request.VotesParJoueur, themes));
        await Clients.Group(session.Id).SendAsync("VotesThemesMisAJour", decompte);
    }

    /// <summary>Reprise du contrôle host (rechargement de la page, reconnexion SignalR) — renvoie l'état
    /// complet de la partie (audit 2026-11-10 : seul le dernier morceau classique était renvoyé, la page
    /// retombait sur le lobby sans joueurs ni progression, et relançait le mauvais audio pendant un
    /// bonus). Voir HostStateSnapshotDto.</summary>
    public async Task<HostStateSnapshotDto> RejoinAsHost(string code, string hostSecret)
    {
        var session = sessionStore.Get(code ?? "") ?? throw new HubException("Partie introuvable.");
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(session.HostSecret), Encoding.UTF8.GetBytes(hostSecret ?? "")))
            throw new HubException("Secret host invalide.");

        await QuitterSessionPrecedenteAsync(code!);
        lock (session.Lock)
        {
            session.HostConnectionId = Context.ConnectionId;
            session.DerniereActiviteA = DateTimeOffset.UtcNow;
        }
        sessionStore.AssocierConnexion(Context.ConnectionId, code!);
        await Groups.AddToGroupAsync(Context.ConnectionId, code!);

        lock (session.Lock) { return ConstruireSnapshotHost(session); }
    }

    public async Task StartRound()
    {
        var session = ResoudreSessionHost();
        Round round;
        Series serie;
        Track track;
        List<QcmOptionDto>? qcmOptions;
        List<string>? anneeOptions;
        List<string> joueursConnectes;
        string? hostConnectionId;

        lock (session.Lock)
        {
            if (session.SeriesList.Count == 0)
                throw new HubException("Aucune série configurée — configure le blindtest (ConfigurerPartie) avant de le démarrer.");
            // Audit 2026-11-10 : StartRound remettait une partie terminée en cours, et un round démarré
            // en pause recevait en plus du temps la pause écoulée avant son démarrage.
            if (session.Etat == GameState.Termine)
                throw new HubException("La partie est terminée.");
            if (session.EnPause)
                throw new HubException("Partie en pause — reprends-la avant de lancer le round.");

            if (session.RoundCourantIndex == -1) session.RoundCourantIndex = 0;

            serie = session.SerieCourante();
            if (session.RoundCourantIndex >= serie.Rounds.Count)
                throw new HubException("Plus de round classique à démarrer dans cette série.");
            if (serie.BonusRound is not null)
                throw new HubException("La question bonus de cette série a déjà commencé.");

            round = serie.Rounds[session.RoundCourantIndex];

            // Retour utilisateur (2026-11-01, « le téléphone demande l'artiste, la TV le titre ») : un
            // second StartRound sur un round déjà lancé retirait la cible au sort en pleine question.
            // Audit 2026-11-10 : relancer un round TERMINÉ retirait aussi cible et options et comptait
            // le round deux fois dans les stats — refusé également (réécouter passe par l'audio local).
            if (round.DebutRound is not null)
            {
                logger.LogWarning("StartRound ignoré — round déjà lancé (partie {Code}, série {Serie}, round {Index}, terminé : {Termine})",
                    session.Id, serie.Index, session.RoundCourantIndex, round.TermineA is not null);
                return;
            }

            track = tracksRepository.GetById(round.TrackId) ?? throw new HubException("Morceau introuvable dans le catalogue.");

            var forcerAnnee = QuotaQuestionsAnnee.PourRound(serie, session.RoundCourantIndex, tracksRepository.GetById, session.Config, Random.Shared);
            roundService.DemarrerRound(round, track, tracksRepository.GetAll(), serie.Tags, session.Config, DateTimeOffset.UtcNow, forcerAnnee);
            session.Etat = GameState.EnCours;
            session.DerniereActiviteA = DateTimeOffset.UtcNow;

            (qcmOptions, round.Options) = ConstruireQcmOptions(round.QcmOptionTrackIds, track, round.Cible, session.Config, tracksRepository);
            anneeOptions = round.AnneeOptions?.Select(a => a.ToString()).ToList();
            joueursConnectes = session.Players.Where(p => p.ConnectionId is not null).Select(p => p.ConnectionId!).ToList();
            hostConnectionId = session.HostConnectionId;
        }

        // Trace de ce qui a réellement été demandé — à recouper avec un retour du type « la TV ne
        // posait pas la même question que le téléphone » (voir FichierLoggerProvider). Les logs ne sont
        // plus servis par /files (audit 2026-11-10), le TrackId n'y est donc pas exposé aux joueurs.
        logger.LogInformation("Round démarré — partie {Code}, série {Serie}, round {Index}, cible {Cible}, mode {Mode}, morceau {TrackId}",
            session.Id, serie.Index, session.RoundCourantIndex, round.Cible, round.Mode, track.Id);

        if (hostConnectionId is not null)
        {
            await Clients.Client(hostConnectionId)
                .SendAsync("RoundStarted", new RoundStartedForHostDto(round.Mode, round.Cible, round.Id, track.Id, track.FilePath, track.RefrainStartMs, serie.Config.DureeFenetreReponseMs, qcmOptions, anneeOptions));
        }

        await Clients.Clients(joueursConnectes)
            .SendAsync("RoundStarted", new RoundStartedForPlayersDto(round.Mode, round.Cible, round.Id, serie.Config.DureeFenetreReponseMs, serie.Index, qcmOptions, TempsEcouleMs: 0, AnneeOptions: anneeOptions));

        timerCoordinator.DemarrerSurveillance(session.Id, serie.Config);

        // Hors verrou et après la diffusion (audit 2026-11-10) : une écriture disque lente ou en échec
        // ne retarde plus RoundStarted alors que le chrono tourne déjà, et ne fige plus la partie.
        IncrementerPlayCount(session.Id, track.Id);
    }

    /// <summary>Retour utilisateur (2026-09-27, bouton « Révéler maintenant » du host) — termine le
    /// round classique courant avant la fin du chrono, une fois que tous les joueurs connectés ont
    /// répondu. Host uniquement. Refusé en pause, hors round, ou tant qu'un joueur connecté n'a pas
    /// répondu — pour ne jamais priver quelqu'un du temps de réponse qu'il lui restait.</summary>
    public async Task RevelerMaintenant()
    {
        var session = ResoudreSessionHost();
        SeriesConfig config;

        lock (session.Lock)
        {
            if (session.EnPause) throw new HubException("Partie en pause.");
            var round = session.RoundCourant();
            if (round?.DebutRound is null || round.TermineA is not null) throw new HubException("Aucun round en cours.");

            var enAttente = session.Players.Count(p => p.EstConnecte && !p.APartie && round.Reponses.All(r => r.PlayerId != p.PlayerId));
            if (enAttente > 0)
                throw new HubException($"{enAttente} joueur(s) n'ont pas encore répondu.");

            config = session.SerieCourante().Config;
        }

        await timerCoordinator.TerminerMaintenantAsync(session, config);
    }

    public async Task StartBonusRound()
    {
        var session = ResoudreSessionHost();
        Series serie;
        BonusStakeOptionsDto options;
        string trackId;

        lock (session.Lock)
        {
            if (session.SeriesList.Count == 0)
                throw new HubException("Aucune série configurée — configure le blindtest (ConfigurerPartie) avant de le démarrer.");
            if (session.Etat == GameState.Termine)
                throw new HubException("La partie est terminée.");
            if (session.EnPause)
                throw new HubException("Partie en pause — reprends-la avant de lancer la question bonus.");

            serie = session.SerieCourante();

            if (serie.BonusRound is not null)
                throw new HubException("La question bonus de cette série a déjà été démarrée.");
            // Audit 2026-11-10 : lancé pendant un round classique, son chrono diffusait RoundEnded en
            // pleine phase de mise.
            if (session.RoundCourant() is { DebutRound: not null, TermineA: null })
                throw new HubException("Un round classique est encore en cours.");

            var dejaUtilises = session.SeriesList
                .SelectMany(s => s.Rounds.Select(r => r.TrackId))
                .Concat(session.SeriesList.Where(s => s.BonusRound is not null).Select(s => s.BonusRound!.TrackId))
                .ToHashSet();
            if (session.Config.ExclureMorceauxSignales) dejaUtilises.UnionWith(flagsRepository.ObtenirTrackIdsBloquants());

            var morceaux = roundService.SelectionnerMorceaux(tracksRepository.GetAll(), serie.Tags, 1, dejaUtilises, statsRepository.GetPlayCount);
            if (morceaux.Count == 0)
                throw new HubException("Pas assez de morceaux disponibles pour la question bonus.");

            var forcerAnnee = QuotaQuestionsAnnee.PourBonus(serie, morceaux[0], session.Config, Random.Shared);
            var bonusRound = bonusRoundService.CreerBonusRound(morceaux[0], tracksRepository.GetAll(), serie.Tags, session.Config, forcerAnnee);
            serie.BonusRound = bonusRound;
            bonusRoundService.DemarrerPhaseMise(bonusRound, DateTimeOffset.UtcNow);
            session.Etat = GameState.EnCours;
            session.DerniereActiviteA = DateTimeOffset.UtcNow;
            trackId = morceaux[0].Id;
            options = new BonusStakeOptionsDto(serie.Config.PaliersDeMise, bonusRound.Id, serie.Config.DureePhaseMiseMs, serie.Index, TempsEcouleMs: 0);
        }

        await Clients.Group(session.Id).SendAsync("BonusStakeOptions", options);

        bonusTimerCoordinator.DemarrerSurveillance(session.Id, serie.Config);
        IncrementerPlayCount(session.Id, trackId);
    }

    // Retour utilisateur (playtest 2026-08-24) : écran "Série B — Rock" diffusé aussi aux joueurs, au
    // même moment que l'écran d'intro du host (avant le premier round de la série, avant StartRound).
    public async Task AnnoncerSerieCourante()
    {
        var session = ResoudreSessionHost();
        SerieAnnonceeDto annonce;
        lock (session.Lock)
        {
            if (session.SeriesList.Count == 0)
                throw new HubException("Aucune série configurée — configure le blindtest (ConfigurerPartie) avant de le démarrer.");

            var serie = session.SerieCourante();
            annonce = new SerieAnnonceeDto(serie.Index, serie.Tags, session.Config.AfficherEcart);
        }

        await Clients.Group(session.Id).SendAsync("SerieAnnoncee", annonce);
    }

    /// <summary>Avance au round suivant, ou à la série suivante une fois le dernier round passé.
    /// Audit 2026-11-10 : aucune garde — appelé pendant un round ou un bonus, il changeait le round
    /// courant sous le chrono, qui sortait sans diffuser RoundEnded/BonusResult (absents non pénalisés,
    /// mises non réglées) ; et après un rechargement du host, il pouvait sauter la question bonus.</summary>
    public Task NextRound()
    {
        var session = ResoudreSessionHost();
        lock (session.Lock)
        {
            if (session.SeriesList.Count == 0)
                throw new HubException("Aucune série configurée.");

            var serie = session.SerieCourante();
            if (session.RoundCourant() is { DebutRound: not null, TermineA: null })
                throw new HubException("Le round en cours n'est pas terminé.");
            if (serie.BonusRound is { TermineA: null })
                throw new HubException("La question bonus en cours n'est pas terminée.");

            if (session.RoundCourantIndex + 1 < serie.Rounds.Count)
            {
                session.RoundCourantIndex++;
            }
            else if (serie.BonusRound is null)
            {
                throw new HubException("La question bonus de cette série n'a pas encore été jouée.");
            }
            else if (session.SerieCouranteIndex + 1 < session.SeriesList.Count)
            {
                session.SerieCouranteIndex++;
                session.RoundCourantIndex = -1;
            }
            else
            {
                // Dernière série épuisée — le host doit appeler EndGame(). On avance quand même le
                // pointeur hors limites pour que StartRound() refuse désormais de rejouer le dernier round.
                session.RoundCourantIndex = serie.Rounds.Count;
            }
            session.DerniereActiviteA = DateTimeOffset.UtcNow;
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
        int nouveauScore;

        lock (session.Lock)
        {
            var round = session.RoundCourant() ?? throw new HubException("Aucun round en cours.");
            var serie = session.SerieCourante();

            resultat = roundService.ValiderManuellement(session, round, serie.Config, request.PlayerId, request.EstCorrecte)
                       ?? throw new HubException("Aucune réponse à valider pour ce joueur sur ce round.");

            nouveauScore = session.Players.First(p => p.PlayerId == request.PlayerId).Score;
        }

        await Clients.Group(session.Id).SendAsync("ScoreUpdate", ScoreDtoBuilder.Construire(session));

        return new RoundAnswerResultDto(resultat.EstCorrecte, resultat.Points, nouveauScore);
    }

    /// <summary>Authentifie le joueur courant comme admin pour sa partie (JoinGame préalable) — un mot
    /// de passe partagé (Admin:RemoteControlPassword, jamais en dur, vide par défaut = désactivé), saisi
    /// dans l'app Flutter. Comparaison à temps constant, au plus TentativesAdminMax essais par connexion.
    /// Le rôle est porté par le joueur (Player.EstAdmin) et survit donc aux reconnexions (audit
    /// 2026-11-10). N'accorde JAMAIS les privilèges host complets (StartRound/ConfigurerPartie restent
    /// exclusifs à ResoudreSessionHost).</summary>
    public Task<AdminAuthResultDto> AuthenticateAdmin(string password)
    {
        var motDePasseConfigure = configuration["Admin:RemoteControlPassword"];
        if (string.IsNullOrEmpty(motDePasseConfigure))
            return Task.FromResult(new AdminAuthResultDto(false, "Contrôle admin désactivé — aucun mot de passe configuré côté serveur."));

        var tentatives = Context.Items.TryGetValue("tentativesAdmin", out var valeur) && valeur is int n ? n : 0;
        if (tentatives >= TentativesAdminMax)
            return Task.FromResult(new AdminAuthResultDto(false, "Trop de tentatives — reconnecte-toi pour réessayer."));

        var estValide = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(motDePasseConfigure), Encoding.UTF8.GetBytes(password ?? ""));
        if (!estValide)
        {
            Context.Items["tentativesAdmin"] = tentatives + 1;
            return Task.FromResult(new AdminAuthResultDto(false, "Mot de passe incorrect."));
        }

        var session = ResoudreSession();
        EtatRegieDto? etatRegie;
        lock (session.Lock)
        {
            ResoudrePlayer(session).EstAdmin = true;
            etatRegie = EtatRegieCourant(session);
        }

        // Refonte UI (lot 3) : télécommande à jour tout de suite, sans attendre le prochain changement
        // d'écran côté host (voir PublierEtatRegie).
        if (etatRegie is not null) _ = Clients.Caller.SendAsync("EtatRegie", etatRegie);
        return Task.FromResult(new AdminAuthResultDto(true, null));
    }

    // ----- Configurations enregistrées (refonte UI, lot 3) -----

    private const int LongueurMaxNomPreset = 40;

    /// <summary>Configurations de partie enregistrées dans data/presets.json — lecture par le host ou
    /// un admin, écriture par le host seul (choix utilisateur).</summary>
    public Task<List<PresetDto>> ListerPresets()
    {
        ResoudreSessionHostOuAdmin();
        return Task.FromResult(ListerPresetsDto());
    }

    /// <summary>Remplace une configuration existante du même nom (sans tenir compte de la casse) et
    /// renvoie la liste à jour.</summary>
    public Task<List<PresetDto>> EnregistrerPreset(PresetDto preset)
    {
        ResoudreSessionHost();
        var nom = (preset.Nom ?? "").Trim();
        if (nom.Length == 0 || nom.Length > LongueurMaxNomPreset)
            throw new HubException($"Nom de configuration requis ({LongueurMaxNomPreset} caractères au plus).");
        if (preset.NombreSeries < 1 || preset.NombreRoundsClassiques < 1 || preset.DureeFenetreReponseMs < 1000 || preset.DelaiEnchainementMs < 1000)
            throw new HubException("Configuration invalide : séries, rounds et durées doivent être positifs.");

        presetsRepository.Enregistrer(new PresetEntryDto
        {
            Nom = nom,
            NombreSeries = preset.NombreSeries,
            NombreRoundsClassiques = preset.NombreRoundsClassiques,
            DureeFenetreReponseMs = preset.DureeFenetreReponseMs,
            ThemesVivier = [.. preset.ThemesVivier ?? []],
            MusiqueContinue = preset.MusiqueContinue,
            DelaiEnchainementMs = preset.DelaiEnchainementMs,
            AfficherEcart = preset.AfficherEcart,
        });
        return Task.FromResult(ListerPresetsDto());
    }

    public Task<List<PresetDto>> SupprimerPreset(string nom)
    {
        ResoudreSessionHost();
        if (!presetsRepository.Supprimer(nom ?? ""))
            throw new HubException("Configuration introuvable.");
        return Task.FromResult(ListerPresetsDto());
    }

    private List<PresetDto> ListerPresetsDto() => presetsRepository.Lister()
        .Select(p => new PresetDto(p.Nom, p.NombreSeries, p.NombreRoundsClassiques, p.DureeFenetreReponseMs, p.ThemesVivier, p.MusiqueContinue, p.DelaiEnchainementMs, p.AfficherEcart))
        .ToList();

    // ----- Télécommande (refonte UI, lot 3) -----

    private static readonly HashSet<string> CommandesHost = ["suivant", "reecouter"];

    /// <summary>Télécommande d'un admin (téléphone) : relaie la commande à la page host, qui l'exécute
    /// comme le raccourci clavier correspondant — l'orchestration de la partie (annonce de série,
    /// enchaînements, audio) reste en un seul endroit, la page host. C'est donc le seul chemin par
    /// lequel un admin influe indirectement sur la lecture (choix assumé de la refonte UI, lot 3).</summary>
    public async Task EnvoyerCommandeHost(string commande)
    {
        var session = ResoudreSessionHostOuAdmin();
        if (!CommandesHost.Contains(commande ?? ""))
            throw new HubException("Commande inconnue.");

        string? hostConnectionId;
        lock (session.Lock) { hostConnectionId = session.HostConnectionId; }
        if (hostConnectionId is null)
            throw new HubException("La page host n'est pas connectée.");

        await Clients.Client(hostConnectionId).SendAsync("CommandeHost", new CommandeHostDto(commande!));
    }

    /// <summary>Publié par la page host à chaque changement de son action suivante — mémorisé en
    /// session et relayé aux admins (jamais aux joueurs).</summary>
    public async Task PublierEtatRegie(EtatRegieDto etat)
    {
        var session = ResoudreSessionHost();
        List<string> admins;
        lock (session.Lock)
        {
            session.RegieLibelleAction = etat.LibelleAction;
            session.RegieActionDisponible = etat.ActionDisponible;
            admins = session.AdminConnectionIds();
        }

        if (admins.Count > 0) await Clients.Clients(admins).SendAsync("EtatRegie", etat);
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

            // Seule une phase chronométrée EN COURS récupère la durée de pause.
            var round = session.RoundCourant();
            if (round is { DebutRound: not null, TermineA: null }) round.DureeEnPauseMs += pauseMs;

            // Audit 2026-11-10 : SerieCourante() plantait sur une partie pas encore configurée.
            var bonusRound = session.SeriesList.Count > 0 ? session.SerieCourante().BonusRound : null;
            if (bonusRound is { TermineA: null } && (bonusRound.DebutPhaseMise is not null || bonusRound.DebutPhaseQuestion is not null))
                bonusRound.DureeEnPauseMs += pauseMs;

            session.EnPause = false;
            session.PauseDemarreeA = null;
        }

        await Clients.Group(session.Id).SendAsync("GameResumed");
    }

    public async Task EndGame()
    {
        var session = ResoudreSessionHostOuAdmin();
        timerCoordinator.Annuler(session.Id);
        bonusTimerCoordinator.Annuler(session.Id);

        GameEndedDto fin;
        lock (session.Lock)
        {
            session.Etat = GameState.Termine;
            session.EnPause = false;
            session.PauseDemarreeA = null;
            fin = ConstruireGameEnded(session);
        }

        await Clients.Group(session.Id).SendAsync("GameEnded", fin);
    }

    /// <summary>Retour utilisateur (2026-09-27) — pendant de RejouerPartie : ferme définitivement ce
    /// salon pour que le host en crée un nouveau (CreateGame, nouveau code). Host uniquement, autorisé
    /// dans tout état. Diffuse SalonFerme aux joueurs puis retire la session du store : un rejoin avec
    /// l'ancien code répond ensuite « Partie introuvable ».</summary>
    public async Task FermerSalon()
    {
        var session = ResoudreSessionHost();
        List<string> connexions;

        lock (session.Lock)
        {
            session.Etat = GameState.Termine;
            connexions = session.Players.Select(p => p.ConnectionId).OfType<string>()
                .Append(Context.ConnectionId)
                .Distinct()
                .ToList();
        }

        timerCoordinator.Annuler(session.Id);
        bonusTimerCoordinator.Annuler(session.Id);

        await Clients.OthersInGroup(session.Id).SendAsync("SalonFerme");

        foreach (var connectionId in connexions)
        {
            sessionStore.DissocierConnexion(connectionId);
            await Groups.RemoveFromGroupAsync(connectionId, session.Id);
        }

        sessionStore.Remove(session.Id);
    }

    /// <summary>Signalement en direct (V2, section 12.4) — host ou admin authentifié uniquement, depuis
    /// l'écran de reveal. Vérifie que le morceau a bien été joué dans cette session (jamais un id
    /// arbitraire) avant d'écrire dans flags.json. La confirmation passe par la valeur de retour
    /// (DejaSignale) ; l'ancien événement MorceauSignale, écouté par aucun client, a été retiré
    /// (audit 2026-11-10).</summary>
    public Task<SignalementResultDto> SignalerMorceau(SignalementRequestDto request)
    {
        var session = ResoudreSessionHostOuAdmin();
        if (!Enum.IsDefined(request.Raison))
            throw new HubException("Raison de signalement inconnue.");
        var commentaire = string.IsNullOrWhiteSpace(request.Commentaire) ? null : request.Commentaire.Trim();
        if (commentaire is { Length: > LongueurMaxCommentaire }) commentaire = commentaire[..LongueurMaxCommentaire];

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
                round = serie.Rounds.FirstOrDefault(r => r.TrackId == request.TrackId && r.DebutRound is not null);
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
            request.TrackId, request.Raison, commentaire, par, session.Id, serieTags, cible, mode);

        return Task.FromResult(new SignalementResultDto(flagId, dejaSignale));
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

            // Retour utilisateur : le vivier d'origine (thèmes distincts toutes séries confondues, [] si
            // "aléatoire" partout) est retiré au sort à nouveau plutôt que de réutiliser l'ordre figé.
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
            session.DerniereActiviteA = DateTimeOffset.UtcNow;

            // Retour utilisateur (2026-10-17) — les joueurs partis en cours de partie ne sont pas réinscrits.
            session.Players.RemoveAll(p => p.APartie);

            foreach (var player in session.Players)
            {
                player.Score = 0;
                player.JokerUtilise = false; // V2, section 12.7 — un joker par partie complète
            }
        }

        await Clients.Group(session.Id).SendAsync("GameRestarted");
    }

    // ----- Méthodes joueur -----

    /// <summary>Rejoint (ou reprend) une partie. <paramref name="jeton"/> : secret remis au premier join
    /// (JoinGameResultDto.Jeton), exigé pour reprendre un PlayerId déjà inscrit (audit 2026-11-10 : les
    /// PlayerId circulent dans tous les DTO publics, n'importe quel client pouvait usurper un joueur).
    /// Si ce joueur était connecté depuis un autre appareil, cette ancienne connexion est détachée et
    /// prévenue (SessionRemplacee) au lieu de pouvoir continuer à répondre en parallèle.</summary>
    public async Task<JoinGameResultDto> JoinGame(string code, string nom, string playerId, string? jeton)
    {
        var session = sessionStore.Get((code ?? "").Trim().ToUpperInvariant());
        if (session is null) return Echec("Partie introuvable.");

        playerId = (playerId ?? "").Trim();
        if (playerId.Length == 0 || playerId.Length > LongueurMaxPlayerId)
            return Echec("Identifiant de joueur invalide — réinstalle ou redémarre l'application.");

        var nomPropre = NettoyerNom(nom);

        Player joueur;
        bool estReconnexion;
        string? ancienneConnexion;
        List<TeamDto> teams;
        List<PlayerSummaryDto> joueurs;
        EtatCourantJoueurDto? etatCourant;
        VoteThemesEtatJoueurDto? voteJoueur;
        bool enPause;
        GameState etat;
        bool afficherEcart;

        lock (session.Lock)
        {
            var existant = session.Players.FirstOrDefault(p => p.PlayerId == playerId);
            estReconnexion = existant is not null;

            if (existant is null)
            {
                if (nomPropre is null)
                    return Echec($"Choisis un nom (1 à {LongueurMaxNomJoueur} caractères).");

                joueur = new Player
                {
                    PlayerId = playerId,
                    Nom = nomPropre,
                    Jeton = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
                    ConnectionId = Context.ConnectionId,
                    EstConnecte = true,
                };
                ancienneConnexion = null;
                session.Players.Add(joueur);
            }
            else
            {
                if (!JetonValide(existant, jeton))
                    return Echec("Ce joueur est déjà inscrit dans la partie depuis un autre appareil.");

                joueur = existant;
                ancienneConnexion = ReassocierConnexion(joueur, Context.ConnectionId);
                // Retour utilisateur (2026-10-17) — un joueur parti (QuitterPartie) qui revient reprend sa
                // place et son score.
                joueur.APartie = false;
            }

            session.DerniereActiviteA = DateTimeOffset.UtcNow;
            teams = session.Teams.Select(t => new TeamDto(t.Id, t.Nom)).ToList();
            joueurs = session.Players.Select(p => new PlayerSummaryDto(p.PlayerId, p.Nom, p.EstConnecte, p.TeamId, p.APartie)).ToList();
            // Retour utilisateur (playtest 2026-09-06) : un joueur qui rejoint en pleine partie reprend la
            // phase en cours — voir ConstruireEtatCourantJoueur.
            etatCourant = ConstruireEtatCourantJoueur(session, playerId);
            voteJoueur = ConstruireVoteThemesJoueur(session, playerId);
            enPause = session.EnPause;
            etat = session.Etat;
            afficherEcart = session.Config.AfficherEcart;
        }

        await QuitterSessionPrecedenteAsync(session.Id);
        await EjecterAncienneConnexionAsync(ancienneConnexion, session.Id);

        // Posé aussi ici (pas seulement dans OnConnectedAsync) pour couvrir le tout premier join.
        Context.Items["playerId"] = playerId;
        sessionStore.AssocierConnexion(Context.ConnectionId, session.Id);
        await Groups.AddToGroupAsync(Context.ConnectionId, session.Id);

        if (estReconnexion)
            await Clients.OthersInGroup(session.Id).SendAsync("PlayerReconnected", new PlayerConnectionChangedDto(joueur.PlayerId, true));
        else
            await Clients.OthersInGroup(session.Id).SendAsync("PlayerJoined", new PlayerJoinedDto(joueur.PlayerId, joueur.Nom));

        await EnvoyerEtatRegieSiAdminAsync(session, joueur);

        return new JoinGameResultDto(true, null, joueur.Score, joueur.TeamId, teams, joueurs, etatCourant, !joueur.JokerUtilise, afficherEcart, voteJoueur,
            joueur.Jeton, enPause, etat, joueur.EstAdmin);

        static JoinGameResultDto Echec(string message) => new(false, message, 0, null, [], [], null);
    }

    /// <summary>Retour utilisateur (2026-10-17) — quitter volontairement la partie depuis l'app. Au lobby,
    /// le joueur est supprimé (votes compris). En cours de partie, il reste au classement avec son score
    /// (Player.APartie) et peut revenir avec le même code, score conservé.</summary>
    public async Task QuitterPartie()
    {
        var session = ResoudreSession();
        Player joueur;
        bool retire;
        VotesThemesDto? decompte = null;

        lock (session.Lock)
        {
            joueur = ResoudrePlayer(session);
            retire = session.Etat == GameState.Lobby;
            joueur.ConnectionId = null;
            joueur.EstConnecte = false;
            joueur.EstAdmin = false;

            if (retire)
            {
                session.Players.Remove(joueur);
                if (session.VoteThemes is { } vote && vote.VotesParPlayerId.Remove(joueur.PlayerId))
                    decompte = ConstruireVotesThemes(vote);
            }
            else
            {
                joueur.APartie = true;
            }
        }

        Context.Items.Remove("playerId");
        sessionStore.DissocierConnexion(Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, session.Id);

        await Clients.Group(session.Id).SendAsync("PlayerLeft", new PlayerLeftDto(joueur.PlayerId, retire));
        if (decompte is not null)
            await Clients.Group(session.Id).SendAsync("VotesThemesMisAJour", decompte);
    }

    /// <summary>Retour utilisateur (2026-10-17) — remplace entièrement les votes de ce joueur tant que le
    /// vote est ouvert. Diffuse le décompte (jamais qui a voté quoi) à tout le groupe.</summary>
    public async Task<bool> VoterThemes(List<string> themes)
    {
        var session = ResoudreSession();
        VotesThemesDto decompte;

        lock (session.Lock)
        {
            var joueur = ResoudrePlayer(session);
            var vote = session.VoteThemes;
            if (vote is null || !vote.Ouvert || session.Etat != GameState.Lobby) return false;

            var choix = (themes ?? []).Distinct().ToList();
            if (choix.Count > vote.VotesParJoueur || choix.Any(t => !vote.ThemesProposes.Contains(t))) return false;

            if (choix.Count == 0) vote.VotesParPlayerId.Remove(joueur.PlayerId);
            else vote.VotesParPlayerId[joueur.PlayerId] = choix;
            decompte = ConstruireVotesThemes(vote);
        }

        await Clients.Group(session.Id).SendAsync("VotesThemesMisAJour", decompte);
        return true;
    }

    /// <summary>Rejoint (ou change) d'équipe — au lobby uniquement (audit 2026-11-10, choix
    /// utilisateur) : en cours de partie, le score d'équipe étant la somme des membres, changer d'équipe
    /// emportait tous les points du joueur vers l'autre équipe.</summary>
    public async Task JoinTeam(string teamId)
    {
        var session = ResoudreSession();
        string playerId;
        string teamIdRetenu;

        lock (session.Lock)
        {
            var joueur = ResoudrePlayer(session);
            if (session.Etat != GameState.Lobby && joueur.TeamId is not null)
                throw new HubException("Les équipes sont figées une fois la partie lancée.");

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
        int score;

        lock (session.Lock)
        {
            joueur = ResoudrePlayer(session);

            var round = session.RoundCourant() ?? throw new HubException("Aucun round en cours.");
            var track = tracksRepository.GetById(round.TrackId) ?? throw new HubException("Morceau introuvable dans le catalogue.");
            var serie = session.SerieCourante();
            var texte = Tronquer(request.Reponse);

            reponse = roundService.SoumettreReponse(session, round, serie.Config, track, joueur.PlayerId, request.RoundId, texte, DateTimeOffset.UtcNow, tracksRepository.GetById);
            if (reponse is null)
            {
                // Retour utilisateur (2026-09-27) : rejet silencieux pour le client mais loggé avec sa raison.
                logger.LogWarning("SubmitAnswer rejeté ({Raison}) — partie {Code}, joueur {PlayerId} ({Nom}), roundId reçu {RoundIdRecu}, round courant {RoundIdCourant}",
                    RaisonRejet(session.EnPause, round.DebutRound is null, round.TermineA is not null, round.Id != request.RoundId, round.Reponses.Any(r => r.PlayerId == joueur.PlayerId), string.IsNullOrWhiteSpace(texte)),
                    session.Id, joueur.PlayerId, joueur.Nom, request.RoundId, round.Id);
                return new RoundAnswerResultDto(false, 0, joueur.Score);
            }

            session.DerniereActiviteA = DateTimeOffset.UtcNow;
            tempsEcouleMs = round.DebutRound is null ? 0 : (int)Math.Max(0, CalculerTempsEcouleMs(session, round.DebutRound.Value, round.DureeEnPauseMs));
            score = joueur.Score;
        }

        // Retour utilisateur : réaction visuelle sur l'écran public dès qu'un joueur répond, avec un
        // classement de rapidité — voir PlayerAnsweredDto pour ce qui est volontairement omis.
        await Clients.Group(session.Id).SendAsync("PlayerAnswered", new PlayerAnsweredDto(joueur.PlayerId, tempsEcouleMs));
        await Clients.Group(session.Id).SendAsync("ScoreUpdate", ScoreDtoBuilder.Construire(session));

        return new RoundAnswerResultDto(reponse.EstCorrecte, reponse.Points, score);
    }

    /// <summary>V2, section 12.7 — un joker par joueur et par partie complète, round classique
    /// uniquement. Toutes les conditions de refus sont vérifiées dans RoundService.UtiliserJoker (pause,
    /// roundId périmé, round terminé, déjà répondu, déjà utilisé).</summary>
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

            // Cover floutée uniquement pour TapeReponse + Titre/Auteur — jamais pour Film (la cover
            // donnerait la réponse) ni Annee.
            if (indice is not null && round.Mode == RoundMode.TapeReponse && round.Cible is RoundCible.Titre or RoundCible.Auteur)
                indice.CoverToken = jokerCoverTokenStore.Emettre(track.Id);
        }

        if (indice is null) throw new HubException("Joker indisponible pour ce round.");

        await Clients.Group(session.Id).SendAsync("JokerUtilise", new JokerUtiliseDto(joueur.PlayerId));
        return ConstruireJokerIndiceDto(indice);
    }

    private static JokerIndiceDto ConstruireJokerIndiceDto(JokerIndice indice) => new(
        indice.OptionsRetirees, indice.TuilesRestantes, indice.Structure, indice.Decennie,
        indice.CoverToken is null ? null : $"/api/joker/cover/{indice.CoverToken}");

    public async Task<bool> SelectStake(SelectStakeRequestDto request)
    {
        var session = ResoudreSession();
        bool enregistree;
        string playerId;
        lock (session.Lock)
        {
            var joueur = ResoudrePlayer(session);
            playerId = joueur.PlayerId;

            if (session.SeriesList.Count == 0) throw new HubException("Aucune question bonus en cours.");
            var serie = session.SerieCourante();
            var bonusRound = serie.BonusRound ?? throw new HubException("Aucune question bonus en cours.");

            enregistree = bonusRoundService.EnregistrerMise(session, bonusRound, serie.Config, joueur.PlayerId, request.RoundId, request.PalierIndex);
            if (enregistree) session.DerniereActiviteA = DateTimeOffset.UtcNow;
        }

        // Retour utilisateur (2026-10-17) — l'écran public montre qui a déjà verrouillé sa mise, jamais
        // le palier choisi (voir MiseEnregistreeDto).
        if (enregistree)
            await Clients.Group(session.Id).SendAsync("MiseEnregistree", new MiseEnregistreeDto(playerId));
        return enregistree;
    }

    public async Task<BonusAnswerResultDto> SubmitBonusAnswer(SubmitBonusAnswerRequestDto request)
    {
        var session = ResoudreSession();
        BonusAnswer? reponse;
        Player joueur;
        int tempsEcouleMs;
        int score;

        lock (session.Lock)
        {
            joueur = ResoudrePlayer(session);

            if (session.SeriesList.Count == 0) throw new HubException("Aucune question bonus en cours.");
            var serie = session.SerieCourante();
            var bonusRound = serie.BonusRound ?? throw new HubException("Aucune question bonus en cours.");
            var track = tracksRepository.GetById(bonusRound.TrackId) ?? throw new HubException("Morceau introuvable dans le catalogue.");
            var texte = Tronquer(request.Reponse);

            reponse = bonusRoundService.SoumettreReponse(session, bonusRound, serie.Config, track, joueur.PlayerId, request.RoundId, texte, DateTimeOffset.UtcNow, tracksRepository.GetById);
            if (reponse is null)
            {
                // Une course déjà tranchée par un autre joueur est un rejet normal, pas une anomalie.
                var dejaRepondu = bonusRound.Reponses.Any(r => r.PlayerId == joueur.PlayerId);
                if (!(bonusRound.EstCourse && bonusRound.Reponses.Count > 0 && !dejaRepondu))
                    logger.LogWarning("SubmitBonusAnswer rejeté ({Raison}) — partie {Code}, joueur {PlayerId} ({Nom}), roundId reçu {RoundIdRecu}, bonus courant {RoundIdCourant}",
                        RaisonRejet(session.EnPause, bonusRound.DebutPhaseQuestion is null, bonusRound.TermineA is not null, bonusRound.Id != request.RoundId, dejaRepondu, string.IsNullOrWhiteSpace(texte)),
                        session.Id, joueur.PlayerId, joueur.Nom, request.RoundId, bonusRound.Id);
                return new BonusAnswerResultDto(false, 0, joueur.Score);
            }

            session.DerniereActiviteA = DateTimeOffset.UtcNow;
            tempsEcouleMs = bonusRound.DebutPhaseQuestion is null
                ? 0
                : (int)Math.Max(0, CalculerTempsEcouleMs(session, bonusRound.DebutPhaseQuestion.Value, bonusRound.DureeEnPauseMs));
            score = joueur.Score;
        }

        await Clients.Group(session.Id).SendAsync("PlayerAnswered", new PlayerAnsweredDto(joueur.PlayerId, tempsEcouleMs));
        await Clients.Group(session.Id).SendAsync("ScoreUpdate", ScoreDtoBuilder.Construire(session));

        return new BonusAnswerResultDto(reponse.EstCorrecte, reponse.Points, score);
    }

    // ----- Cycle de connexion -----

    /// <summary>Reconnexion automatique (V2) : le client (joueur) ouvre le hub avec
    /// "/hubs/game?code=XXXX&amp;playerId=...&amp;jeton=..." dès que le code de partie est connu — SignalR
    /// réutilise cette URL à chaque reconnexion transport, donc ce handler rattache le joueur SANS que
    /// le client ait besoin de rappeler JoinGame. Le jeton est exigé (audit 2026-11-10). Ne concerne
    /// jamais le host, qui passe par RejoinAsHost (authentifié par hostSecret).</summary>
    public override async Task OnConnectedAsync()
    {
        var query = Context.GetHttpContext()?.Request.Query;
        var code = query?["code"].ToString();
        var playerId = query?["playerId"].ToString();
        var jeton = query?["jeton"].ToString();

        if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(playerId) && sessionStore.Get(code) is { } session)
        {
            Player? joueur;
            string? ancienneConnexion = null;
            EtatCourantConnexionDto? etat = null;
            lock (session.Lock)
            {
                // Un joueur parti (QuitterPartie) n'est jamais rattaché en silence : seul un JoinGame
                // explicite le fait revenir.
                joueur = session.Players.FirstOrDefault(p => p.PlayerId == playerId && !p.APartie);
                if (joueur is not null && !JetonValide(joueur, jeton)) joueur = null;
                if (joueur is not null)
                {
                    ancienneConnexion = ReassocierConnexion(joueur, Context.ConnectionId);
                    session.DerniereActiviteA = DateTimeOffset.UtcNow;
                    etat = new EtatCourantConnexionDto(joueur.Score, joueur.TeamId, ConstruireEtatCourantJoueur(session, playerId),
                        !joueur.JokerUtilise, session.Config.AfficherEcart, ConstruireVoteThemesJoueur(session, playerId),
                        session.EnPause, session.Etat, joueur.EstAdmin);
                }
            }

            if (joueur is not null && etat is not null)
            {
                logger.LogInformation("Reconnexion automatique — partie {Code}, joueur {PlayerId} ({Nom})", code, playerId, joueur.Nom);
                await EjecterAncienneConnexionAsync(ancienneConnexion, code);
                Context.Items["playerId"] = playerId;
                sessionStore.AssocierConnexion(Context.ConnectionId, code);
                await Groups.AddToGroupAsync(Context.ConnectionId, code);

                await Clients.Caller.SendAsync("EtatCourant", etat);
                await Clients.OthersInGroup(code).SendAsync("PlayerReconnected", new PlayerConnectionChangedDto(playerId, true));
                await EnvoyerEtatRegieSiAdminAsync(session, joueur);
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
                // Audit 2026-11-10 : HostConnectionId n'était jamais remis à null, la télécommande
                // admin partait dans le vide sans erreur pendant une coupure du host.
                if (session.HostConnectionId == connectionId) session.HostConnectionId = null;
            }

            if (playerId is not null)
            {
                logger.LogInformation("Déconnexion — partie {Code}, joueur {PlayerId}, cause : {Cause}", code, playerId, exception?.Message ?? "fermeture normale");

                // Délai de grâce (V2, GameConfig.DelaiGraceDeconnexionMs) : une micro-coupure suivie
                // d'une reconnexion automatique ne doit pas faire clignoter l'indicateur côté host.
                var delaiMs = session.Config.DelaiGraceDeconnexionMs;
                if (delaiMs > 0) await Task.Delay(delaiMs);

                bool toujoursDeconnecte;
                lock (session.Lock)
                {
                    var joueur = session.Players.FirstOrDefault(p => p.PlayerId == playerId);
                    toujoursDeconnecte = joueur is not null && joueur.ConnectionId == connectionId;
                    if (toujoursDeconnecte)
                    {
                        joueur!.EstConnecte = false;
                        joueur.ConnectionId = null;
                    }
                }

                if (toujoursDeconnecte)
                    await Clients.Group(code).SendAsync("PlayerDisconnected", new PlayerConnectionChangedDto(playerId, false));
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    // ----- Aides privées -----

    private void IncrementerPlayCount(string code, string trackId)
    {
        try { statsRepository.IncrementPlayCount(trackId); }
        catch (Exception ex) { logger.LogError(ex, "playCount non enregistré — partie {Code}, morceau {TrackId}", code, trackId); }
    }

    /// <summary>Une connexion qui crée/rejoint un salon quitte d'abord celui auquel elle était
    /// éventuellement associée (audit 2026-11-10 : elle restait dans les deux groupes et recevait les
    /// événements des deux parties).</summary>
    private async Task QuitterSessionPrecedenteAsync(string nouveauCode)
    {
        var codePrecedent = sessionStore.ObtenirCodeParConnexion(Context.ConnectionId);
        if (codePrecedent is null || codePrecedent == nouveauCode) return;

        if (sessionStore.Get(codePrecedent) is { } ancienne)
        {
            lock (ancienne.Lock)
            {
                foreach (var p in ancienne.Players.Where(p => p.ConnectionId == Context.ConnectionId))
                {
                    p.ConnectionId = null;
                    p.EstConnecte = false;
                }
                if (ancienne.HostConnectionId == Context.ConnectionId) ancienne.HostConnectionId = null;
            }
        }
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, codePrecedent);
        sessionStore.DissocierConnexion(Context.ConnectionId);
    }

    private async Task EjecterAncienneConnexionAsync(string? ancienneConnexion, string code)
    {
        if (ancienneConnexion is null || ancienneConnexion == Context.ConnectionId) return;
        sessionStore.DissocierConnexion(ancienneConnexion);
        await Groups.RemoveFromGroupAsync(ancienneConnexion, code);
        await Clients.Client(ancienneConnexion).SendAsync("SessionRemplacee");
    }

    private async Task EnvoyerEtatRegieSiAdminAsync(GameSession session, Player joueur)
    {
        EtatRegieDto? etat;
        lock (session.Lock) { etat = joueur.EstAdmin ? EtatRegieCourant(session) : null; }
        if (etat is not null) await Clients.Caller.SendAsync("EtatRegie", etat);
    }

    private static EtatRegieDto? EtatRegieCourant(GameSession session) =>
        session.RegieLibelleAction is null ? null : new EtatRegieDto(session.RegieLibelleAction, session.RegieActionDisponible);

    private static bool JetonValide(Player joueur, string? jeton) =>
        !string.IsNullOrEmpty(jeton)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(joueur.Jeton), Encoding.UTF8.GetBytes(jeton));

    /// <summary>Nom affiché d'un nouveau joueur : espaces normalisés, caractères de contrôle retirés,
    /// 1 à LongueurMaxNomJoueur caractères (audit 2026-11-10 : aucune validation, un nom piégé
    /// exécutait du script sur l'écran TV). Null si rien d'exploitable.</summary>
    private static string? NettoyerNom(string? nom)
    {
        var propre = new string((nom ?? "").Where(c => !char.IsControl(c)).ToArray());
        propre = string.Join(' ', propre.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (propre.Length == 0) return null;
        return propre.Length > LongueurMaxNomJoueur ? propre[..LongueurMaxNomJoueur].TrimEnd() : propre;
    }

    private static string Tronquer(string? reponse)
    {
        var texte = reponse ?? "";
        return texte.Length > LongueurMaxReponse ? texte[..LongueurMaxReponse] : texte;
    }

    /// <summary>Décompte public du vote : nombre de voix par thème voté (plus voté d'abord), qui a voté,
    /// jamais qui a voté quoi. Appelé sous session.Lock.</summary>
    private static VotesThemesDto ConstruireVotesThemes(VoteThemes vote)
    {
        var votes = vote.VotesParPlayerId.Values.SelectMany(v => v)
            .GroupBy(t => t)
            .Select(g => new VoteThemeCompteDto(g.Key, g.Count()))
            .OrderByDescending(v => v.Nombre).ThenBy(v => v.Theme, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new VotesThemesDto(vote.Ouvert, votes, vote.VotesParPlayerId.Keys.ToList(), votes.Count, vote.ThemesRetenus?.ToList());
    }

    /// <summary>Appelé sous session.Lock.</summary>
    private static VoteThemesEtatJoueurDto? ConstruireVoteThemesJoueur(GameSession session, string playerId) =>
        session.VoteThemes is { } vote
            ? new VoteThemesEtatJoueurDto(vote.Ouvert, vote.VotesParJoueur, vote.ThemesProposes,
                vote.VotesParPlayerId.TryGetValue(playerId, out var mesVotes) ? mesVotes : [])
            : null;

    private static string RaisonRejet(bool enPause, bool nonDemarre, bool termine, bool roundIdPerime, bool dejaRepondu, bool vide) =>
        enPause ? "partie en pause"
        : roundIdPerime ? "roundId périmé"
        : nonDemarre ? "phase de réponse non démarrée"
        : termine ? "phase déjà terminée"
        : dejaRepondu ? "déjà répondu"
        : vide ? "réponse vide"
        : "autre";

    /// <summary>Rattache le joueur à sa nouvelle connexion et renvoie l'ancienne (à détacher), s'il en
    /// avait une autre.</summary>
    private static string? ReassocierConnexion(Player joueur, string connectionId)
    {
        var ancienne = joueur.ConnectionId;
        joueur.ConnectionId = connectionId;
        joueur.EstConnecte = true;
        return ancienne != connectionId ? ancienne : null;
    }

    /// <summary>Retrouve le joueur courant via l'identité posée par JoinGame/OnConnectedAsync
    /// (Context.Items["playerId"]) — et vérifie que cette connexion est toujours la sienne (audit
    /// 2026-11-10 : après une reprise depuis un autre appareil, l'ancienne connexion pouvait encore
    /// répondre pour le même joueur). Appelé sous session.Lock par tous ses appelants.</summary>
    private Player ResoudrePlayer(GameSession session)
    {
        var playerId = Context.Items.TryGetValue("playerId", out var value) ? value as string : null;
        if (playerId is null)
            throw new HubException("Joueur non reconnu dans cette partie.");

        var joueur = session.Players.FirstOrDefault(p => p.PlayerId == playerId)
                     ?? throw new HubException("Joueur non reconnu dans cette partie.");
        if (joueur.ConnectionId != Context.ConnectionId)
            throw new HubException("Ta session a été reprise sur un autre appareil.");
        return joueur;
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
        lock (session.Lock)
        {
            if (session.HostConnectionId != Context.ConnectionId)
                throw new HubException("Seul le host peut effectuer cette action.");
            session.DerniereActiviteA = DateTimeOffset.UtcNow;
        }
        return session;
    }

    /// <summary>Version élargie de ResoudreSessionHost — accepte aussi un joueur authentifié admin
    /// (Player.EstAdmin, voir AuthenticateAdmin) : pause/reprise/tableau général/fin de partie/
    /// télécommande/signalement. Tout le reste du cycle de jeu (StartRound, ConfigurerPartie,
    /// CreateGame...) reste exclusif au host via ResoudreSessionHost.</summary>
    private GameSession ResoudreSessionHostOuAdmin()
    {
        var session = ResoudreSession();
        lock (session.Lock)
        {
            var estAdmin = session.Players.Any(p => p.EstAdmin && p.ConnectionId == Context.ConnectionId);
            if (session.HostConnectionId != Context.ConnectionId && !estAdmin)
                throw new HubException("Seul le host ou un admin authentifié peut effectuer cette action.");
            session.DerniereActiviteA = DateTimeOffset.UtcNow;
        }
        return session;
    }

    /// <summary>Appelé sous session.Lock.</summary>
    private GameEndedDto ConstruireGameEnded(GameSession session)
    {
        var titres = TitresService.CalculerTitres(session, tracksRepository.GetById)
            .Select(t => new TitreDto(t.Code, t.Libelle, t.Description, t.PlayerIds))
            .ToList();
        return new GameEndedDto(ScoreDtoBuilder.Construire(session), titres);
    }

    /// <summary>DTO BonusQuestionStarted côté host — partagé avec BonusTimerCoordinator (diffusion) et
    /// le snapshot de reprise.</summary>
    internal static BonusQuestionStartedForHostDto BonusQuestionPourHost(GameSession session, BonusRound bonusRound, Track track, SeriesConfig config, List<QcmOptionDto>? qcmOptions, List<string>? anneeOptions) =>
        new(track.Id, track.FilePath, track.RefrainStartMs, bonusRound.Id, config.DureePhaseQuestionMs,
            session.Config.RalentissementBonusActive, session.Config.FacteurRalentissementBonus,
            bonusRound.Mode, bonusRound.Cible, qcmOptions, bonusRound.EstCourse, anneeOptions);

    /// <summary>Snapshot complet pour RejoinAsHost — appelé sous session.Lock. Voir HostStateSnapshotDto.</summary>
    private HostStateSnapshotDto ConstruireSnapshotHost(GameSession session)
    {
        var configuree = session.SeriesList.Count > 0;
        var serie = configuree ? session.SerieCourante() : null;
        var round = session.RoundCourant();
        var bonus = serie?.BonusRound;

        var phase = PhaseHost.Lobby;
        RoundStartedForHostDto? roundDto = null;
        RoundEndedDto? roundEnded = null;
        BonusStakeOptionsDto? bonusMise = null;
        List<string>? mises = null;
        BonusQuestionStartedForHostDto? bonusQuestion = null;
        BonusResultDto? bonusResult = null;
        GameEndedDto? gameEnded = null;
        long? positionAudio = null;
        long? tempsEcoule = null;
        List<PlayerAnsweredDto>? repondants = null;

        if (session.Etat == GameState.Termine)
        {
            phase = PhaseHost.Termine;
            gameEnded = ConstruireGameEnded(session);
        }
        else if (session.Etat == GameState.EnCours && serie is not null)
        {
            if (bonus is not null)
            {
                if (bonus.TermineA is not null)
                {
                    phase = PhaseHost.BonusTermine;
                    bonusResult = RevealDtoBuilder.BonusResult(bonus, tracksRepository);
                }
                else if (bonus.DebutPhaseQuestion is not null && tracksRepository.GetById(bonus.TrackId) is { } trackBonus)
                {
                    phase = PhaseHost.BonusQuestion;
                    var qcm = bonus.Options?.Select(o => new QcmOptionDto(o.OptionId, o.TexteAffiche)).ToList();
                    bonusQuestion = BonusQuestionPourHost(session, bonus, trackBonus, serie.Config, qcm, bonus.AnneeOptions?.Select(a => a.ToString()).ToList());
                    tempsEcoule = Math.Max(0, CalculerTempsEcouleMs(session, bonus.DebutPhaseQuestion.Value, bonus.DureeEnPauseMs));
                    var vitesse = session.Config.RalentissementBonusActive ? session.Config.FacteurRalentissementBonus : 1;
                    positionAudio = (long)(tempsEcoule.Value * vitesse);
                    repondants = bonus.Reponses.Where(r => !r.EstAbsent).OrderBy(r => r.Timestamp)
                        .Select(r => new PlayerAnsweredDto(r.PlayerId, r.TempsReponseMs)).ToList();
                }
                else if (bonus.DebutPhaseMise is not null)
                {
                    phase = PhaseHost.BonusMise;
                    tempsEcoule = Math.Max(0, CalculerTempsEcouleMs(session, bonus.DebutPhaseMise.Value, bonus.DureeEnPauseMs));
                    bonusMise = new BonusStakeOptionsDto(serie.Config.PaliersDeMise, bonus.Id, serie.Config.DureePhaseMiseMs, serie.Index, (int)tempsEcoule.Value);
                    mises = bonus.Mises.Select(m => m.PlayerId).ToList();
                }
            }
            else if (round?.DebutRound is not null && round.TermineA is null && tracksRepository.GetById(round.TrackId) is { } track)
            {
                phase = PhaseHost.Round;
                var qcm = round.Options?.Select(o => new QcmOptionDto(o.OptionId, o.TexteAffiche)).ToList();
                roundDto = new RoundStartedForHostDto(round.Mode, round.Cible, round.Id, track.Id, track.FilePath, track.RefrainStartMs,
                    serie.Config.DureeFenetreReponseMs, qcm, round.AnneeOptions?.Select(a => a.ToString()).ToList());
                tempsEcoule = Math.Max(0, CalculerTempsEcouleMs(session, round.DebutRound.Value, round.DureeEnPauseMs));
                positionAudio = tempsEcoule;
                repondants = round.Reponses.Where(r => !r.EstAbsent).OrderBy(r => r.Timestamp)
                    .Select(r => new PlayerAnsweredDto(r.PlayerId, r.TempsReponseMs)).ToList();
            }
            else if (round?.TermineA is not null)
            {
                phase = PhaseHost.RoundTermine;
                roundEnded = RevealDtoBuilder.RoundEnded(session, round, tracksRepository);
            }
            else if (round is not null && session.RoundCourantIndex > 0)
            {
                // NextRound déjà appelé, StartRound pas encore : on remontre le reveal précédent.
                phase = PhaseHost.RoundPret;
                roundEnded = RevealDtoBuilder.RoundEnded(session, serie.Rounds[session.RoundCourantIndex - 1], tracksRepository);
            }
            else
            {
                phase = PhaseHost.AvantSerie;
            }
        }

        var roundsDemarres = serie?.Rounds.Count(r => r.DebutRound is not null) ?? 0;

        return new HostStateSnapshotDto(
            session.EnPause,
            phase,
            configuree,
            session.SeriesList.Count,
            serie?.Rounds.Count ?? 0,
            session.SerieCouranteIndex,
            roundsDemarres,
            session.SeriesList.Select(s => s.Tags.ToList()).ToList(),
            session.Players.Select(p => new PlayerSummaryDto(p.PlayerId, p.Nom, p.EstConnecte, p.TeamId, p.APartie)).ToList(),
            session.Teams.Select(t => new TeamDto(t.Id, t.Nom)).ToList(),
            ScoreDtoBuilder.Construire(session),
            session.VoteThemes is { } vote ? ConstruireVotesThemes(vote) : null,
            roundDto,
            roundEnded,
            bonusMise,
            mises,
            bonusQuestion,
            bonusResult,
            gameEnded,
            positionAudio,
            tempsEcoule,
            repondants);
    }

    /// <summary>Feinte QCM purement visuelle (retour utilisateur) — voir GameConfig.ProbabiliteQcmFeinteChamp.
    /// Remplace le texte affiché d'un distracteur tiré au sort par le champ opposé du morceau correct
    /// (cible Titre → l'artiste du bon morceau, cible Auteur → son titre), sans toucher à son TrackId : le
    /// sélectionner reste une mauvaise réponse normale. Retourne le TrackId de l'option modifiée.</summary>
    internal static string? AppliquerFeinteEventuelle(List<OptionEnConstruction> options, Track correct, RoundCible cible, GameConfig config)
    {
        // Pas de dualité "champ opposé" pertinente pour Film/Année.
        if (cible is not (RoundCible.Titre or RoundCible.Auteur)) return null;
        if (Random.Shared.NextDouble() >= config.ProbabiliteQcmFeinteChamp) return null;

        var distracteurs = options.Where(o => o.TrackId != correct.Id).ToList();
        if (distracteurs.Count == 0) return null;

        var optionChoisie = distracteurs[Random.Shared.Next(distracteurs.Count)];
        var texte = cible == RoundCible.Titre ? AuteurVariantes.Premier(correct) : correct.Title;
        // Jamais un doublon visible avec une autre option (même règle que QcmGenerator).
        if (options.Any(o => string.Equals(o.Texte, texte, StringComparison.OrdinalIgnoreCase))) return null;

        optionChoisie.Texte = texte;
        return optionChoisie.TrackId;
    }

    /// <summary>Feinte texte inventé (retour utilisateur, ex. Bastille - Pompéi -> "Baptiste") — voir
    /// GameConfig.ProbabiliteQcmFeinteTexteArtiste. Le texte vient de Track.TrapTextArtist, un leurre écrit
    /// à la main. Cible Auteur uniquement. Retourne le TrackId modifié ou null.</summary>
    internal static string? AppliquerFeinteTexteEventuelle(List<OptionEnConstruction> options, Track correct, RoundCible cible, GameConfig config)
    {
        if (cible != RoundCible.Auteur || string.IsNullOrEmpty(correct.TrapTextArtist)) return null;
        if (Random.Shared.NextDouble() >= config.ProbabiliteQcmFeinteTexteArtiste) return null;
        if (options.Any(o => string.Equals(o.Texte, correct.TrapTextArtist, StringComparison.OrdinalIgnoreCase))) return null;

        var distracteurs = options.Where(o => o.TrackId != correct.Id && o.TrackId != o.FeinteTrackId).ToList();
        if (distracteurs.Count == 0) return null;

        var optionChoisie = distracteurs[Random.Shared.Next(distracteurs.Count)];
        optionChoisie.Texte = correct.TrapTextArtist;
        return optionChoisie.TrackId;
    }

    internal sealed class OptionEnConstruction(string trackId, string texte)
    {
        public string TrackId { get; } = trackId;
        public string Texte { get; set; } = texte;
        public string? FeinteTrackId { get; set; }
    }

    /// <summary>Construit à la fois les DTOs envoyés au réseau (identifiant opaque + texte affiché) et
    /// leur miroir persistable (Round.Options/BonusRound.Options, V2) — feintes tirées une seule fois par
    /// round. Partagé avec BonusTimerCoordinator (question bonus).</summary>
    internal static (List<QcmOptionDto>? QcmOptions, List<RoundOption>? Options) ConstruireQcmOptions(
        List<string>? qcmOptionTrackIds, Track correct, RoundCible cible, GameConfig config, ITracksRepository tracksRepository)
    {
        if (qcmOptionTrackIds is null) return (null, null);

        var enConstruction = qcmOptionTrackIds
            .Select(id => tracksRepository.GetById(id))
            .OfType<Track>()
            .Select(t => new OptionEnConstruction(t.Id, TexteAffichePourCible(t, cible)))
            .ToList();

        var feinteTrackIds = new HashSet<string>();
        var feinteChamp = AppliquerFeinteEventuelle(enConstruction, correct, cible, config);
        if (feinteChamp is not null)
        {
            feinteTrackIds.Add(feinteChamp);
            foreach (var o in enConstruction) o.FeinteTrackId = feinteChamp;
        }
        var feinteTexte = AppliquerFeinteTexteEventuelle(enConstruction, correct, cible, config);
        if (feinteTexte is not null) feinteTrackIds.Add(feinteTexte);

        var options = enConstruction.Select(o => new RoundOption
        {
            TrackId = o.TrackId,
            OptionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant(),
            TexteAffiche = o.Texte,
            EstFeinte = feinteTrackIds.Contains(o.TrackId),
            EstPiege = correct.TrapWith.Contains(o.TrackId),
        }).ToList();

        return (options.Select(o => new QcmOptionDto(o.OptionId, o.TexteAffiche)).ToList(), options);
    }

    /// <summary>Texte affiché pour la cible — premier artiste crédité en cible Auteur (calculé ici,
    /// audit 2026-11-10 : les clients découpaient eux-mêmes le champ Artist sur la virgule).</summary>
    private static string TexteAffichePourCible(Track track, RoundCible cible) => cible switch
    {
        RoundCible.Auteur => AuteurVariantes.Premier(track),
        RoundCible.Film => FilmNameResolver.Resoudre(track),
        _ => track.Title,
    };

    /// <summary>Reconstruit, pour un joueur qui (re)rejoint, la phase actuellement active (round
    /// classique ou question bonus) afin qu'il puisse répondre immédiatement — voir
    /// EtatCourantJoueurDto. Appelé sous session.Lock. Null si rien n'est activement en cours. Les
    /// options QCM sont relues sur Round.Options/BonusRound.Options : exactement les mêmes (feintes et
    /// identifiants compris) que celles vues par les autres joueurs.</summary>
    private EtatCourantJoueurDto? ConstruireEtatCourantJoueur(GameSession session, string playerId)
    {
        if (session.Etat != GameState.EnCours || session.SeriesList.Count == 0) return null;

        var serie = session.SerieCourante();
        var bonusRound = serie.BonusRound;

        if (bonusRound is { TermineA: null, DebutPhaseQuestion: not null })
        {
            var finAnticipee = bonusRound.EstCourse && bonusRound.Reponses.Count > 0;
            var ecouleMs = CalculerTempsEcouleMs(session, bonusRound.DebutPhaseQuestion.Value, bonusRound.DureeEnPauseMs);
            if (!finAnticipee && ecouleMs < serie.Config.DureePhaseQuestionMs)
            {
                var qcmOptions = bonusRound.Options?.Select(o => new QcmOptionDto(o.OptionId, o.TexteAffiche)).ToList();
                var anneeOptions = bonusRound.AnneeOptions?.Select(a => a.ToString()).ToList();
                var dejaRepondu = bonusRound.Reponses.Any(r => r.PlayerId == playerId);
                var dto = new BonusQuestionStartedForPlayersDto(bonusRound.Id, serie.Config.DureePhaseQuestionMs, bonusRound.Cible, serie.Index, bonusRound.Mode, qcmOptions, bonusRound.EstCourse, (int)Math.Max(0, ecouleMs), anneeOptions);
                return new EtatCourantJoueurDto(PhaseJoueur.BonusQuestion, session.EnPause, dejaRepondu, null, null, dto);
            }
        }
        else if (bonusRound is { TermineA: null, DebutPhaseQuestion: null, DebutPhaseMise: not null })
        {
            var ecouleMs = CalculerTempsEcouleMs(session, bonusRound.DebutPhaseMise.Value, bonusRound.DureeEnPauseMs);
            if (ecouleMs < serie.Config.DureePhaseMiseMs)
            {
                var dejaMise = bonusRound.Mises.Any(m => m.PlayerId == playerId);
                var dto = new BonusStakeOptionsDto(serie.Config.PaliersDeMise, bonusRound.Id, serie.Config.DureePhaseMiseMs, serie.Index, (int)Math.Max(0, ecouleMs));
                return new EtatCourantJoueurDto(PhaseJoueur.BonusMise, session.EnPause, dejaMise, null, dto, null);
            }
        }

        if (bonusRound is not null) return null;

        var round = session.RoundCourant();
        if (round is { DebutRound: not null, TermineA: null })
        {
            var ecouleMs = CalculerTempsEcouleMs(session, round.DebutRound.Value, round.DureeEnPauseMs);
            if (ecouleMs < serie.Config.DureeFenetreReponseMs)
            {
                var qcmOptions = round.Options?.Select(o => new QcmOptionDto(o.OptionId, o.TexteAffiche)).ToList();
                var anneeOptions = round.AnneeOptions?.Select(a => a.ToString()).ToList();
                var dejaRepondu = round.Reponses.Any(r => r.PlayerId == playerId);
                // V2, section 12.7 : rejoue le MÊME indice si ce joueur avait déjà utilisé son joker sur ce round.
                var jokerIndice = round.JokerIndicesParJoueur.TryGetValue(playerId, out var indiceExistant)
                    ? ConstruireJokerIndiceDto(indiceExistant)
                    : null;
                var dto = new RoundStartedForPlayersDto(round.Mode, round.Cible, round.Id, serie.Config.DureeFenetreReponseMs, serie.Index, qcmOptions, (int)Math.Max(0, ecouleMs), anneeOptions, jokerIndice);
                return new EtatCourantJoueurDto(PhaseJoueur.RoundClassique, session.EnPause, dejaRepondu, dto, null, null);
            }
        }

        return null;
    }

    /// <summary>Temps écoulé depuis <paramref name="debut"/>, hors pauses — réutilisé pour un BonusRound
    /// (DebutPhaseMise/DebutPhaseQuestion) comme pour un Round.</summary>
    private static long CalculerTempsEcouleMs(GameSession session, DateTimeOffset debut, long dureeEnPauseMs)
    {
        var pauseEnCoursMs = session.EnPause && session.PauseDemarreeA is not null
            ? (DateTimeOffset.UtcNow - session.PauseDemarreeA.Value).TotalMilliseconds
            : 0;

        return (long)((DateTimeOffset.UtcNow - debut).TotalMilliseconds - (dureeEnPauseMs + pauseEnCoursMs));
    }
}
