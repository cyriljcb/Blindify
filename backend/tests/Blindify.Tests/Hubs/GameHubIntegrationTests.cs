using Blindify.Api.Contracts;
using Blindify.Domain.Configuration;
using Blindify.Domain.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Blindify.Tests.Hubs;

public class GameHubIntegrationTests : IClassFixture<GameHubTestFactory>, IAsyncLifetime
{
    private readonly GameHubTestFactory _factory;
    private HubConnection _hostConnection = null!;
    private HubConnection _playerConnection = null!;

    public GameHubIntegrationTests(GameHubTestFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _hostConnection = _factory.CreateHubConnection();
        _playerConnection = _factory.CreateHubConnection();
        await _hostConnection.StartAsync();
        await _playerConnection.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _hostConnection.DisposeAsync();
        await _playerConnection.DisposeAsync();
    }

    // CreateGame ne configure plus rien (retour utilisateur du 2026-08-24 : permettre de recréer une
    // configuration ratée sans recréer le lobby) — regroupe CreateGame + ConfigurerPartie pour garder
    // les tests concis, comme l'ancien CreateGame combiné. ConfigurerPartieRequestDto est une
    // intention (nombre de séries/rounds/durée + vivier de thèmes) depuis docs/refactor-decisions.md
    // section 1 — le serveur (SeriesPlanner) calcule désormais la répartition des thèmes, les paliers
    // de mise et le mode de chaque round, qui n'est donc plus imposable directement par le test.
    private static async Task<CreateGameResultDto> CreerEtConfigurerPartie(
        HubConnection host, bool modeEquipe, int nombreRoundsClassiques, List<string> themesVivier, GameConfig? config,
        List<string>? nomsEquipes = null, int dureePhaseMiseMs = 5000, int dureePhaseQuestionMs = 5000)
    {
        var creation = await host.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(modeEquipe, nomsEquipes));
        await host.InvokeAsync("ConfigurerPartie", new ConfigurerPartieRequestDto(
            NombreSeries: 1,
            NombreRoundsClassiques: nombreRoundsClassiques,
            DureeFenetreReponseMs: 800,
            ThemesVivier: themesVivier,
            Config: config,
            DureePhaseMiseMs: dureePhaseMiseMs,
            DureePhaseQuestionMs: dureePhaseQuestionMs));
        return creation;
    }

    [Fact]
    public async Task PartieComplete_CreateJoinStartAnswer_ProduitLesEvenementsAttendus()
    {
        var scoreUpdates = new List<ScoreUpdateDto>();
        var roundEndedTcs = new TaskCompletionSource<RoundEndedDto>();
        RoundStartedForPlayersDto? roundStartedPlayer = null;
        RoundStartedForHostDto? roundStartedHost = null;

        _playerConnection.On<ScoreUpdateDto>("ScoreUpdate", scores => scoreUpdates.Add(scores));
        _playerConnection.On<RoundStartedForPlayersDto>("RoundStarted", payload => roundStartedPlayer = payload);
        _playerConnection.On<RoundEndedDto>("RoundEnded", payload => roundEndedTcs.TrySetResult(payload));
        _hostConnection.On<RoundStartedForHostDto>("RoundStarted", payload => roundStartedHost = payload);

        // Round.Mode est désormais tiré aléatoirement côté serveur (SeriesPlanner.PickRandomRoundModes,
        // docs/refactor-decisions.md section 1) — ce test a besoin d'un round Qcm pour pouvoir asserter
        // sur QcmOptions ; on relance la création de partie jusqu'à l'obtenir plutôt que de figer
        // artificiellement le mode (même stratégie que QcmFeinteTexteArtiste_ProbabiliteMaximale...
        // ci-dessous pour la combinaison Cible/TrackId).
        CreateGameResultDto creation = null!;
        JoinGameResultDto join = null!;
        for (var tentative = 0; tentative < 50; tentative++)
        {
            roundStartedHost = null;
            roundStartedPlayer = null;
            creation = await CreerEtConfigurerPartie(_hostConnection, false, 1, [], null);
            join = await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-1");
            await _hostConnection.InvokeAsync("StartRound");
            await AttendreAsync(() => roundStartedPlayer is not null && roundStartedHost is not null);
            if (roundStartedHost!.Mode == RoundMode.Qcm) break;

            // Tentative abandonnée : annule le minuteur de round de fond avant de recréer une partie,
            // sinon son RoundEnded tardif (naturel, personne n'a répondu) arrive plus tard sur cette
            // même connexion (restée membre du groupe de la partie abandonnée) et pollue le
            // roundEndedTcs ci-dessous avec le mauvais morceau.
            await _hostConnection.InvokeAsync("EndGame");
        }

        Assert.Equal(5, creation.Code.Length);
        Assert.True(join.Success);
        Assert.Equal(0, join.Score);

        Assert.Equal(RoundMode.Qcm, roundStartedPlayer!.Mode);
        Assert.NotNull(roundStartedPlayer.QcmOptions);
        Assert.Equal(4, roundStartedPlayer.QcmOptions!.Count);
        Assert.NotNull(roundStartedHost!.FilePath);

        var bonneOption = roundStartedPlayer.QcmOptions.First(o => o.TrackId == roundStartedHost.TrackId);

        var resultat = await _playerConnection.InvokeAsync<RoundAnswerResultDto>("SubmitAnswer", new SubmitAnswerRequestDto(bonneOption.TrackId));

        Assert.True(resultat.EstCorrecte);
        Assert.True(resultat.Points > 0);
        Assert.Equal(resultat.Points, resultat.NouveauScore);

        var roundEnded = await AvecTimeout(roundEndedTcs.Task, TimeSpan.FromSeconds(5));

        Assert.Equal(roundStartedHost.TrackId, roundEnded.TrackId);
        var entreeAlice = roundEnded.Resultats.Single(r => r.PlayerId == "player-1");
        Assert.True(entreeAlice.EstCorrecte);

        Assert.True(scoreUpdates.Count > 0);
        var alice = scoreUpdates[^1].Joueurs.Single(j => j.PlayerId == "player-1");
        Assert.Equal(resultat.NouveauScore, alice.Score);
        Assert.Null(scoreUpdates[^1].Equipes);
    }

    [Fact]
    public async Task NextRound_ApresLeDernierRoundDeLaSerie_StartRoundEstRefuse()
    {
        var roundEndedTcs = new TaskCompletionSource<RoundEndedDto>();
        _hostConnection.On<RoundEndedDto>("RoundEnded", payload => roundEndedTcs.TrySetResult(payload));

        await CreerEtConfigurerPartie(_hostConnection, false, 1, [], null);

        await _hostConnection.InvokeAsync("StartRound");
        await AvecTimeout(roundEndedTcs.Task, TimeSpan.FromSeconds(5));

        // Un seul round dans l'unique série : plus rien à démarrer après NextRound().
        await _hostConnection.InvokeAsync("NextRound");

        var exception = await Assert.ThrowsAsync<HubException>(() => _hostConnection.InvokeAsync("StartRound"));
        Assert.Contains("Plus de round classique", exception.Message);
    }

    [Fact]
    public async Task AnnoncerSerieCourante_DiffuseIndexEtTagsDeLaSerieEnCoursATousLesClients()
    {
        var annonceHostTcs = new TaskCompletionSource<SerieAnnonceeDto>();
        var annoncePlayerTcs = new TaskCompletionSource<SerieAnnonceeDto>();
        _hostConnection.On<SerieAnnonceeDto>("SerieAnnoncee", payload => annonceHostTcs.TrySetResult(payload));
        _playerConnection.On<SerieAnnonceeDto>("SerieAnnoncee", payload => annoncePlayerTcs.TrySetResult(payload));

        // Catalogue de test réduit à des morceaux "disney" sans tags (voir GameHubTestFactory) —
        // thème vide ("aléatoire") pour ne pas dépendre d'un tag qui n'existe pas dans ce catalogue.
        var creation = await CreerEtConfigurerPartie(_hostConnection, false, 1, [], null);
        await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-1");

        await _hostConnection.InvokeAsync("AnnoncerSerieCourante");

        var annonceHost = await AvecTimeout(annonceHostTcs.Task, TimeSpan.FromSeconds(5));
        var annoncePlayer = await AvecTimeout(annoncePlayerTcs.Task, TimeSpan.FromSeconds(5));

        Assert.Equal(0, annonceHost.SerieIndex);
        Assert.Empty(annonceHost.Tags);
        Assert.Equal(0, annoncePlayer.SerieIndex);
        Assert.Empty(annoncePlayer.Tags);
    }

    [Fact]
    public async Task ConfigurerPartie_ApresLeDemarrage_EstRefusee()
    {
        await CreerEtConfigurerPartie(_hostConnection, false, 1, [], null);
        await _hostConnection.InvokeAsync("StartRound");

        var exception = await Assert.ThrowsAsync<HubException>(() => _hostConnection.InvokeAsync(
            "ConfigurerPartie", new ConfigurerPartieRequestDto(1, 1, 800, [], null, DureePhaseMiseMs: 5000, DureePhaseQuestionMs: 5000)));
        Assert.Contains("déjà démarré", exception.Message);
    }

    [Fact]
    public async Task StartRound_SansConfigurationPrealable_EstRefuse()
    {
        await _hostConnection.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(ModeEquipe: false));

        var exception = await Assert.ThrowsAsync<HubException>(() => _hostConnection.InvokeAsync("StartRound"));
        Assert.Contains("Aucune série configurée", exception.Message);
    }

    [Fact]
    public async Task ConfigurerPartie_RappeleeAvantLeDemarrage_RemplaceEntierementLaConfigurationPrecedente()
    {
        await _hostConnection.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(ModeEquipe: false));

        // Premier essai raté (trop de rounds demandés pour le thème "disney" du catalogue de test,
        // voir GameHubTestFactory : seulement 4 morceaux) — ne doit pas laisser de configuration
        // partielle derrière lui.
        var premierEssai = await Assert.ThrowsAsync<HubException>(() => _hostConnection.InvokeAsync(
            "ConfigurerPartie", new ConfigurerPartieRequestDto(1, 10, 800, [], null, DureePhaseMiseMs: 5000, DureePhaseQuestionMs: 5000)));
        Assert.Contains("seulement", premierEssai.Message);

        var exceptionAvantReconfig = await Assert.ThrowsAsync<HubException>(() => _hostConnection.InvokeAsync("StartRound"));
        Assert.Contains("Aucune série configurée", exceptionAvantReconfig.Message);

        // Reconfiguration réussie, sans recréer le lobby.
        await _hostConnection.InvokeAsync(
            "ConfigurerPartie", new ConfigurerPartieRequestDto(1, 1, 800, [], null, DureePhaseMiseMs: 5000, DureePhaseQuestionMs: 5000));

        RoundStartedForHostDto? roundStartedHost = null;
        _hostConnection.On<RoundStartedForHostDto>("RoundStarted", payload => roundStartedHost = payload);
        await _hostConnection.InvokeAsync("StartRound");
        await AttendreAsync(() => roundStartedHost is not null);
    }

    [Fact]
    public async Task RejouerPartie_ApresFinDePartie_ResetLesScoresEtPermetDeRedemarrer()
    {
        var roundEndedTcs = new TaskCompletionSource<RoundEndedDto>();
        var gameRestartedTcs = new TaskCompletionSource();
        RoundStartedForHostDto? roundStartedApresReplay = null;

        _hostConnection.On<RoundEndedDto>("RoundEnded", payload => roundEndedTcs.TrySetResult(payload));
        _hostConnection.On("GameRestarted", () => gameRestartedTcs.TrySetResult());

        RoundStartedForPlayersDto? roundStartedPlayer = null;
        RoundStartedForHostDto? roundStartedHost = null;
        _playerConnection.On<RoundStartedForPlayersDto>("RoundStarted", payload => roundStartedPlayer = payload);
        _hostConnection.On<RoundStartedForHostDto>("RoundStarted", payload => roundStartedHost = payload);

        CreateGameResultDto creation = null!;
        for (var tentative = 0; tentative < 50; tentative++)
        {
            roundStartedHost = null;
            roundStartedPlayer = null;
            creation = await CreerEtConfigurerPartie(_hostConnection, false, 1, [], null);
            await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-1");
            await _hostConnection.InvokeAsync("StartRound");
            await AttendreAsync(() => roundStartedPlayer is not null && roundStartedHost is not null);
            if (roundStartedHost!.Mode == RoundMode.Qcm) break;

            // Voir commentaire équivalent dans PartieComplete_... ci-dessus.
            await _hostConnection.InvokeAsync("EndGame");
        }

        var bonneOption = roundStartedPlayer!.QcmOptions!.First(o => o.TrackId == roundStartedHost!.TrackId);
        var resultat = await _playerConnection.InvokeAsync<RoundAnswerResultDto>("SubmitAnswer", new SubmitAnswerRequestDto(bonneOption.TrackId));
        await AvecTimeout(roundEndedTcs.Task, TimeSpan.FromSeconds(5));

        await _hostConnection.InvokeAsync("EndGame");
        await _hostConnection.InvokeAsync("RejouerPartie");
        await AttendreAsync(() => gameRestartedTcs.Task.IsCompleted);

        // Reconnexion avec le même playerId : le score doit être remis à zéro.
        var rejoin = await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-1");
        Assert.Equal(0, rejoin.Score);

        // La partie doit pouvoir redémarrer sans repasser par CreateGame/ConfigurerPartie — RejouerPartie
        // conserve la configuration déjà soumise (mêmes séries/tags, nouvelle sélection de morceaux).
        _hostConnection.On<RoundStartedForHostDto>("RoundStarted", payload => roundStartedApresReplay = payload);
        await _hostConnection.InvokeAsync("StartRound");
        await AttendreAsync(() => roundStartedApresReplay is not null);

        Assert.NotNull(roundStartedApresReplay);
        Assert.True(resultat.Points > 0);
    }

    [Fact]
    public async Task AuthenticateAdmin_MotDePasseCorrect_PermetPauseSansDelogerLeHost()
    {
        var creation = await CreerEtConfigurerPartie(_hostConnection, false, 1, [], null);
        await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-1");

        var auth = await _playerConnection.InvokeAsync<AdminAuthResultDto>("AuthenticateAdmin", "test-admin-pw");
        Assert.True(auth.Success);

        var gamePausedTcs = new TaskCompletionSource();
        _hostConnection.On("GamePaused", () => gamePausedTcs.TrySetResult());
        await _playerConnection.InvokeAsync("PauseGame");
        await AttendreAsync(() => gamePausedTcs.Task.IsCompleted);

        // Le host garde la main malgré l'authentification admin d'une autre connexion (pas de
        // HubException ici — HostConnectionId n'a jamais été réassocié, contrairement à RejoinAsHost).
        await _hostConnection.InvokeAsync("ResumeGame");
    }

    [Fact]
    public async Task AuthenticateAdmin_MotDePasseIncorrect_NAccordeAucunPrivilege()
    {
        var creation = await CreerEtConfigurerPartie(_hostConnection, false, 1, [], null);
        await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-1");

        var auth = await _playerConnection.InvokeAsync<AdminAuthResultDto>("AuthenticateAdmin", "mauvais-mot-de-passe");
        Assert.False(auth.Success);
        Assert.NotNull(auth.ErrorMessage);

        await Assert.ThrowsAsync<HubException>(() => _playerConnection.InvokeAsync("PauseGame"));
    }

    [Fact]
    public async Task SubmitAnswer_DiffusePlayerAnswered_SansRevelerLaReponse()
    {
        RoundStartedForPlayersDto? roundStartedPlayer = null;
        _playerConnection.On<RoundStartedForPlayersDto>("RoundStarted", payload => roundStartedPlayer = payload);
        var playerAnsweredTcs = new TaskCompletionSource<PlayerAnsweredDto>();
        _hostConnection.On<PlayerAnsweredDto>("PlayerAnswered", payload => playerAnsweredTcs.TrySetResult(payload));

        var creation = await CreerEtConfigurerPartie(_hostConnection, false, 1, [], null);
        await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-1");
        await _hostConnection.InvokeAsync("StartRound");
        await AttendreAsync(() => roundStartedPlayer is not null);

        // N'importe quelle réponse (juste ou fausse) déclenche l'évènement — seule l'absence de
        // réponse (reponse is null côté RoundService) ne le déclenche pas.
        await _playerConnection.InvokeAsync<RoundAnswerResultDto>("SubmitAnswer", new SubmitAnswerRequestDto("n'importe quoi"));

        var playerAnswered = await AvecTimeout(playerAnsweredTcs.Task, TimeSpan.FromSeconds(5));
        Assert.Equal("player-1", playerAnswered.PlayerId);
        Assert.True(playerAnswered.TempsEcouleMs >= 0);
    }

    [Fact]
    public async Task ModeEquipe_CreationEtJoinTeam_AgregeLeScoreParEquipe()
    {
        RoundStartedForPlayersDto? roundStartedPlayer = null;
        RoundStartedForHostDto? roundStartedHost = null;
        var scoreUpdates = new List<ScoreUpdateDto>();
        _playerConnection.On<RoundStartedForPlayersDto>("RoundStarted", payload => roundStartedPlayer = payload);
        _hostConnection.On<RoundStartedForHostDto>("RoundStarted", payload => roundStartedHost = payload);
        _playerConnection.On<ScoreUpdateDto>("ScoreUpdate", scores => scoreUpdates.Add(scores));

        CreateGameResultDto creation = null!;
        TeamDto equipeRouge = null!;
        for (var tentative = 0; tentative < 50; tentative++)
        {
            roundStartedHost = null;
            roundStartedPlayer = null;

            creation = await CreerEtConfigurerPartie(_hostConnection, true, 1, [], null, nomsEquipes: ["Rouge", "Bleu"]);
            equipeRouge = creation.Teams.First(t => t.Nom == "Rouge");

            var join = await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-1");
            if (tentative == 0)
            {
                Assert.Equal(2, creation.Teams.Count);
                Assert.Contains(creation.Teams, t => t.Nom == "Rouge");
                Assert.Equal(2, join.Teams.Count);
                Assert.Null(join.TeamId); // pas encore rejoint d'équipe
            }

            await _playerConnection.InvokeAsync("JoinTeam", equipeRouge.Id);
            await _hostConnection.InvokeAsync("StartRound");
            await AttendreAsync(() => roundStartedPlayer is not null && roundStartedHost is not null);
            if (roundStartedHost!.Mode == RoundMode.Qcm) break;

            // Voir commentaire équivalent dans PartieComplete_... ci-dessus.
            await _hostConnection.InvokeAsync("EndGame");
        }

        var bonneOption = roundStartedPlayer!.QcmOptions!.First(o => o.TrackId == roundStartedHost!.TrackId);
        var resultat = await _playerConnection.InvokeAsync<RoundAnswerResultDto>("SubmitAnswer", new SubmitAnswerRequestDto(bonneOption.TrackId));

        await AttendreAsync(() => scoreUpdates.Count > 0);
        var dernier = scoreUpdates[^1];
        Assert.NotNull(dernier.Equipes);
        var scoreRouge = dernier.Equipes!.Single(e => e.TeamId == equipeRouge.Id);
        Assert.Equal(resultat.Points, scoreRouge.Score);
        var scoreBleu = dernier.Equipes!.Single(e => e.Nom == "Bleu");
        Assert.Equal(0, scoreBleu.Score);
    }

    [Fact]
    public async Task QcmFeinteChamp_ProbabiliteMaximale_UnDistracteurAfficheLeChampOpposeDuMorceauCorrect()
    {
        // Catalogue de test (GameHubTestFactory) : 4 morceaux, artistes tous distincts.
        var titresParId = new Dictionary<string, string>
        {
            ["t1"] = "Under the Sea",
            ["t2"] = "Circle of Life",
            ["t3"] = "Let It Go",
            ["t4"] = "Hakuna Matata"
        };
        var auteursParId = new Dictionary<string, string>
        {
            ["t1"] = "Samuel E. Wright",
            ["t2"] = "Elton John",
            ["t3"] = "Idina Menzel",
            ["t4"] = "Nathan Lane"
        };

        RoundStartedForPlayersDto? roundStartedPlayer = null;
        RoundStartedForHostDto? roundStartedHost = null;
        _playerConnection.On<RoundStartedForPlayersDto>("RoundStarted", payload => roundStartedPlayer = payload);
        _hostConnection.On<RoundStartedForHostDto>("RoundStarted", payload => roundStartedHost = payload);

        var config = new GameConfig { ProbabiliteQcmPiege = 0, ProbabiliteQcmFeinteChamp = 1.0 };

        for (var tentative = 0; tentative < 50; tentative++)
        {
            roundStartedHost = null;
            roundStartedPlayer = null;
            var creation = await CreerEtConfigurerPartie(_hostConnection, false, 1, [], config);
            await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", $"player-champ-{tentative}");
            await _hostConnection.InvokeAsync("StartRound");
            await AttendreAsync(() => roundStartedPlayer is not null && roundStartedHost is not null);
            if (roundStartedHost!.Mode == RoundMode.Qcm) break;

            // Voir commentaire équivalent dans PartieComplete_... ci-dessus.
            await _hostConnection.InvokeAsync("EndGame");
        }

        var correctId = roundStartedHost!.TrackId;
        var champAttendu = roundStartedHost.Cible == RoundCible.Titre ? auteursParId[correctId] : titresParId[correctId];

        var distracteurs = roundStartedPlayer!.QcmOptions!.Where(o => o.TrackId != correctId).ToList();
        var champAffiche = roundStartedHost.Cible == RoundCible.Titre
            ? distracteurs.Select(o => o.Title)
            : distracteurs.Select(o => o.Artist);

        Assert.Contains(champAttendu, champAffiche);
    }

    [Fact]
    public async Task QcmFeinteTexteArtiste_ProbabiliteMaximale_UnDistracteurAfficheLeLeurreInvente()
    {
        // Seul t1 (voir GameHubTestFactory) a un TrapTextArtist renseigné. Le morceau du round est
        // tiré au hasard dans les 4 morceaux du catalogue (RoundService.SelectionnerMorceaux), la
        // cible (Titre/Auteur) l'est aussi séparément à 50/50 (RoundService.DemarrerRound), et depuis
        // docs/refactor-decisions.md section 1 le Mode l'est également côté serveur
        // (SeriesPlanner.PickRandomRoundModes) : on relance des parties jusqu'à tomber sur
        // (t1, Auteur, Qcm), seule combinaison où la feinte s'applique et où QcmOptions est peuplé.
        RoundStartedForPlayersDto? roundStartedPlayer = null;
        RoundStartedForHostDto? roundStartedHost = null;
        _playerConnection.On<RoundStartedForPlayersDto>("RoundStarted", payload => roundStartedPlayer = payload);
        _hostConnection.On<RoundStartedForHostDto>("RoundStarted", payload => roundStartedHost = payload);

        var config = new GameConfig { ProbabiliteQcmPiege = 0, ProbabiliteQcmFeinteChamp = 0, ProbabiliteQcmFeinteTexteArtiste = 1.0 };

        bool Trouve() => roundStartedHost is { Cible: RoundCible.Auteur, TrackId: "t1", Mode: RoundMode.Qcm };

        for (var tentative = 0; tentative < 200 && !Trouve(); tentative++)
        {
            roundStartedPlayer = null;
            roundStartedHost = null;

            var creation = await CreerEtConfigurerPartie(_hostConnection, false, 1, [], config);

            await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", $"player-texte-{tentative}");
            await _hostConnection.InvokeAsync("StartRound");
            await AttendreAsync(() => roundStartedPlayer is not null && roundStartedHost is not null);
            if (!Trouve()) await _hostConnection.InvokeAsync("EndGame");
        }

        Assert.True(Trouve(), "Pas obtenu (t1, Auteur, Qcm) en 200 tentatives.");

        var distracteurs = roundStartedPlayer!.QcmOptions!.Where(o => o.TrackId != roundStartedHost!.TrackId);
        Assert.Contains("Faux Artiste Test", distracteurs.Select(o => o.Artist));
    }

    [Fact]
    public async Task JoinGame_RenvoieLeRosterCompletYComprisSoiMeme()
    {
        var creation = await CreerEtConfigurerPartie(_hostConnection, false, 1, [], null);

        // Retour utilisateur : un joueur seul se voyait comme "0 joueur connecté" (PlayerJoined
        // n'est diffusé qu'aux AUTRES joueurs déjà présents) — le roster renvoyé par JoinGame
        // doit inclure le joueur qui vient de rejoindre, pas seulement ceux déjà là avant lui.
        var joinAlice = await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-1");
        Assert.Single(joinAlice.Joueurs);
        Assert.Equal("player-1", joinAlice.Joueurs[0].PlayerId);

        await using var autreConnexion = _factory.CreateHubConnection();
        await autreConnexion.StartAsync();
        var joinBob = await autreConnexion.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Bob", "player-2");

        Assert.Equal(2, joinBob.Joueurs.Count);
        Assert.Contains(joinBob.Joueurs, p => p.PlayerId == "player-1");
        Assert.Contains(joinBob.Joueurs, p => p.PlayerId == "player-2");
    }

    [Fact]
    public async Task JoinGame_CodeInconnu_RetourneUnEchec()
    {
        var join = await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", "ZZZZZ", "Bob", "player-2");

        Assert.False(join.Success);
        Assert.NotNull(join.ErrorMessage);
    }

    [Fact]
    public async Task JoinGame_ReconnexionPendantUnRoundClassiqueEnCours_RenvoieLetatCourantPourRejoindreLeRoundActif()
    {
        // Retour utilisateur (playtest 2026-09-06) : un joueur qui rejoignait en pleine partie
        // atterrissait au lobby en attendant le round suivant, sans pouvoir répondre à celui déjà
        // en cours. Simule un redémarrage de l'appli (nouvelle connexion SignalR, même playerId
        // stable persisté côté client) pendant qu'un round tourne encore.
        RoundStartedForPlayersDto? roundStartedPlayer = null;
        _playerConnection.On<RoundStartedForPlayersDto>("RoundStarted", payload => roundStartedPlayer = payload);

        var creation = await CreerEtConfigurerPartie(_hostConnection, false, 1, [], null);
        await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-reco-1");
        await _hostConnection.InvokeAsync("StartRound");
        await AttendreAsync(() => roundStartedPlayer is not null);

        await using var reconnexion = _factory.CreateHubConnection();
        await reconnexion.StartAsync();
        var rejoin = await reconnexion.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-reco-1");

        Assert.True(rejoin.Success);
        Assert.NotNull(rejoin.EtatCourant);
        Assert.Equal(PhaseJoueur.RoundClassique, rejoin.EtatCourant!.Phase);
        Assert.False(rejoin.EtatCourant.DejaRepondu);
        Assert.False(rejoin.EtatCourant.EnPause);
        Assert.NotNull(rejoin.EtatCourant.Round);
        Assert.Equal(roundStartedPlayer!.Mode, rejoin.EtatCourant.Round!.Mode);
        Assert.Equal(roundStartedPlayer.Cible, rejoin.EtatCourant.Round.Cible);
        Assert.Null(rejoin.EtatCourant.BonusMise);
        Assert.Null(rejoin.EtatCourant.BonusQuestion);
    }

    [Fact]
    public async Task JoinGame_ReconnexionApresAvoirDejaRepondu_IndiqueDejaReponduPourNePasRouvrirLaSaisie()
    {
        RoundStartedForPlayersDto? roundStartedPlayer = null;
        _playerConnection.On<RoundStartedForPlayersDto>("RoundStarted", payload => roundStartedPlayer = payload);

        var creation = await CreerEtConfigurerPartie(_hostConnection, false, 1, [], null);
        await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-reco-2");
        await _hostConnection.InvokeAsync("StartRound");
        await AttendreAsync(() => roundStartedPlayer is not null);

        // Peu importe le mode ou la justesse de la réponse ici : un seul essai est déjà consommé
        // dès la première soumission (RoundService.SoumettreReponse), correcte ou non.
        await _playerConnection.InvokeAsync<RoundAnswerResultDto>("SubmitAnswer", new SubmitAnswerRequestDto("peu importe"));

        await using var reconnexion = _factory.CreateHubConnection();
        await reconnexion.StartAsync();
        var rejoin = await reconnexion.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-reco-2");

        Assert.NotNull(rejoin.EtatCourant);
        Assert.Equal(PhaseJoueur.RoundClassique, rejoin.EtatCourant!.Phase);
        Assert.True(rejoin.EtatCourant.DejaRepondu);
    }

    [Fact]
    public async Task JoinGame_AvantLeDemarrageDeLaPartie_EtatCourantEstNull()
    {
        var creation = await CreerEtConfigurerPartie(_hostConnection, false, 1, [], null);
        var join = await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-lobby");

        Assert.Null(join.EtatCourant);
    }

    [Fact]
    public async Task JoinGame_ReconnexionPendantLaMiseBonusEnCours_RenvoieLetatCourantPourRejoindreLaMise()
    {
        // StartBonusRound n'exige pas que les rounds classiques de la série soient épuisés, mais
        // session.Etat ne passe à EnCours que via StartRound (voir ConstruireEtatCourantJoueur) —
        // en jeu réel toujours vrai (au moins un round classique précède la question bonus).
        BonusStakeOptionsDto? stakeOptions = null;
        _playerConnection.On<BonusStakeOptionsDto>("BonusStakeOptions", payload => stakeOptions = payload);

        var creation = await CreerEtConfigurerPartie(_hostConnection, false, 1, [], null);
        await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-bonus-mise-1");
        await _hostConnection.InvokeAsync("StartRound");
        await _hostConnection.InvokeAsync("StartBonusRound");
        await AttendreAsync(() => stakeOptions is not null);

        await using var reconnexion = _factory.CreateHubConnection();
        await reconnexion.StartAsync();
        var rejoin = await reconnexion.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-bonus-mise-1");

        Assert.NotNull(rejoin.EtatCourant);
        Assert.Equal(PhaseJoueur.BonusMise, rejoin.EtatCourant!.Phase);
        Assert.False(rejoin.EtatCourant.DejaRepondu);
        Assert.NotNull(rejoin.EtatCourant.BonusMise);
        Assert.Equal(stakeOptions!.Paliers, rejoin.EtatCourant.BonusMise!.Paliers);
        Assert.Null(rejoin.EtatCourant.Round);
        Assert.Null(rejoin.EtatCourant.BonusQuestion);
    }

    [Fact]
    public async Task JoinGame_ReconnexionPendantLaQuestionBonusEnCours_RenvoieLetatCourantPourRejoindreLaQuestion()
    {
        BonusStakeOptionsDto? stakeOptions = null;
        BonusQuestionStartedForPlayersDto? questionStarted = null;
        _playerConnection.On<BonusStakeOptionsDto>("BonusStakeOptions", payload => stakeOptions = payload);
        _playerConnection.On<BonusQuestionStartedForPlayersDto>("BonusQuestionStarted", payload => questionStarted = payload);

        // Mise raccourcie pour ne pas attendre 5s en pure perte : personne ne mise explicitement,
        // le palier par défaut ("safe") est appliqué automatiquement à l'échéance
        // (BonusTimerCoordinator.AppliquerPaliersParDefaut) puis la phase question démarre.
        var creation = await CreerEtConfigurerPartie(_hostConnection, false, 1, [], null, dureePhaseMiseMs: 200, dureePhaseQuestionMs: 5000);
        await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-bonus-q-1");
        await _hostConnection.InvokeAsync("StartRound");
        await _hostConnection.InvokeAsync("StartBonusRound");
        await AttendreAsync(() => stakeOptions is not null);
        await AttendreAsync(() => questionStarted is not null, timeoutMs: 3000);

        await using var reconnexion = _factory.CreateHubConnection();
        await reconnexion.StartAsync();
        var rejoin = await reconnexion.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-bonus-q-1");

        Assert.NotNull(rejoin.EtatCourant);
        Assert.Equal(PhaseJoueur.BonusQuestion, rejoin.EtatCourant!.Phase);
        Assert.False(rejoin.EtatCourant.DejaRepondu);
        Assert.NotNull(rejoin.EtatCourant.BonusQuestion);
        Assert.Equal(questionStarted!.Mode, rejoin.EtatCourant.BonusQuestion!.Mode);
        Assert.Null(rejoin.EtatCourant.Round);
        Assert.Null(rejoin.EtatCourant.BonusMise);
    }

    [Fact]
    public async Task PauseResume_PendantUnRound_RejetteLesReponsesPendantLaPauseEtLesReaccepteApresReprise()
    {
        var gamePausedTcs = new TaskCompletionSource();
        var gameResumedTcs = new TaskCompletionSource();
        RoundStartedForPlayersDto? roundStartedPlayer = null;
        RoundStartedForHostDto? roundStartedHost = null;

        _hostConnection.On("GamePaused", () => gamePausedTcs.TrySetResult());
        _hostConnection.On("GameResumed", () => gameResumedTcs.TrySetResult());
        _playerConnection.On<RoundStartedForPlayersDto>("RoundStarted", payload => roundStartedPlayer = payload);
        _hostConnection.On<RoundStartedForHostDto>("RoundStarted", payload => roundStartedHost = payload);

        for (var tentative = 0; tentative < 50; tentative++)
        {
            roundStartedHost = null;
            roundStartedPlayer = null;
            var creation = await CreerEtConfigurerPartie(_hostConnection, false, 1, [], null);
            await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", $"player-pause-{tentative}");
            await _hostConnection.InvokeAsync("StartRound");
            await AttendreAsync(() => roundStartedPlayer is not null && roundStartedHost is not null);
            if (roundStartedHost!.Mode == RoundMode.Qcm) break;

            // Voir commentaire équivalent dans PartieComplete_... ci-dessus.
            await _hostConnection.InvokeAsync("EndGame");
        }

        await _hostConnection.InvokeAsync("PauseGame");
        await AttendreAsync(() => gamePausedTcs.Task.IsCompleted);

        // Filet de sécurité serveur (architecture.md section 9) : toute soumission reçue pendant
        // EnPause=true est rejetée, pas juste désactivée côté UI — et ne consomme pas l'unique
        // essai du joueur (RoundService.SoumettreReponse retourne avant d'enregistrer la réponse).
        var bonneOption = roundStartedPlayer!.QcmOptions!.First(o => o.TrackId == roundStartedHost!.TrackId);
        var reponsePendantPause = await _playerConnection.InvokeAsync<RoundAnswerResultDto>(
            "SubmitAnswer", new SubmitAnswerRequestDto(bonneOption.TrackId));
        Assert.False(reponsePendantPause.EstCorrecte);
        Assert.Equal(0, reponsePendantPause.Points);

        await Task.Delay(150); // laisse s'écouler du temps de pause à neutraliser dans DureeEnPauseMs
        await _hostConnection.InvokeAsync("ResumeGame");
        await AttendreAsync(() => gameResumedTcs.Task.IsCompleted);

        // Après reprise, l'essai du joueur est toujours disponible et accepté normalement.
        var resultat = await _playerConnection.InvokeAsync<RoundAnswerResultDto>(
            "SubmitAnswer", new SubmitAnswerRequestDto(bonneOption.TrackId));
        Assert.True(resultat.EstCorrecte);
        Assert.True(resultat.Points > 0);
    }

    [Fact]
    public async Task RejoinAsHost_ExigeLeHostSecret_RefuseUnIntrusEtResynchroniseLeVraiHost()
    {
        RoundStartedForHostDto? roundStartedHost = null;
        _hostConnection.On<RoundStartedForHostDto>("RoundStarted", payload => roundStartedHost = payload);

        var creation = await CreerEtConfigurerPartie(_hostConnection, false, 1, [], null);

        await _hostConnection.InvokeAsync("StartRound");
        await AttendreAsync(() => roundStartedHost is not null);

        // Un client qui ne connaît que le code de partie (comme n'importe quel joueur) ne doit
        // pas pouvoir usurper le rôle host sans le HostSecret renvoyé à la création.
        await using var intrus = _factory.CreateHubConnection();
        await intrus.StartAsync();
        var exception = await Assert.ThrowsAsync<HubException>(
            () => intrus.InvokeAsync<HostStateSnapshotDto>("RejoinAsHost", creation.Code, "mauvais-secret"));
        Assert.Contains("Secret host invalide", exception.Message);

        // Le vrai host (nouvelle connexion, ex. après un refresh) reprend la main avec le bon secret,
        // et l'état renvoyé permet de reprendre l'audio sans redémarrer le morceau.
        await using var nouvelleConnexionHost = _factory.CreateHubConnection();
        await nouvelleConnexionHost.StartAsync();
        var snapshot = await nouvelleConnexionHost.InvokeAsync<HostStateSnapshotDto>(
            "RejoinAsHost", creation.Code, creation.HostSecret);

        Assert.False(snapshot.EnPause);
        Assert.Equal(roundStartedHost!.TrackId, snapshot.TrackId);
        Assert.Equal(roundStartedHost.FilePath, snapshot.FilePath);
        Assert.NotNull(snapshot.PositionAudioMs);
        Assert.True(snapshot.PositionAudioMs >= 0);

        // La nouvelle connexion est désormais reconnue comme host par le serveur.
        await nouvelleConnexionHost.InvokeAsync("PauseGame");
    }

    [Fact]
    public async Task RejoinAsHost_LobbySansConfigurationEncoreSoumise_NeCrashePas()
    {
        // Retour utilisateur (playtest 2026-08-24) : ce sous-état (lobby existant, aucune série
        // encore configurée) n'était pas atteignable avant que CreateGame ne configure plus rien —
        // GameSessionNavigation.RoundCourant() doit rester défensif sur une SeriesList vide.
        var creation = await _hostConnection.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(ModeEquipe: false));

        var snapshot = await _hostConnection.InvokeAsync<HostStateSnapshotDto>("RejoinAsHost", creation.Code, creation.HostSecret);

        Assert.False(snapshot.EnPause);
        Assert.Null(snapshot.TrackId);
    }

    private static async Task AttendreAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var depart = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - depart).TotalMilliseconds > timeoutMs)
                throw new TimeoutException("Condition non remplie dans le délai imparti.");
            await Task.Delay(25);
        }
    }

    private static async Task<T> AvecTimeout<T>(Task<T> task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task) throw new TimeoutException();
        return await task;
    }
}
