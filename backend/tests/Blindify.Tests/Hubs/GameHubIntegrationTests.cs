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

    private static SeriesConfig NouveauSeriesConfig(int nombreRounds) => new()
    {
        NombreRoundsClassiques = nombreRounds,
        DureeFenetreReponseMs = 800,
        PointsMax = 100,
        PointsMin = 20,
        PenaliteMauvaiseReponseRatio = 0.5,
        PenaliteAbsenceReponse = -5,
        PaliersDeMise = [10, 20, 30, 50],
        DureePhaseMiseMs = 5000,
        DureePhaseQuestionMs = 5000
    };

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

        var creation = await _hostConnection.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(
            Tags: [],
            ModeEquipe: false,
            SeriesSetups: [new SeriesSetupDto(NouveauSeriesConfig(1), [RoundMode.Qcm])],
            Config: null));

        Assert.Equal(5, creation.Code.Length);

        var join = await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-1");
        Assert.True(join.Success);
        Assert.Equal(0, join.Score);

        await _hostConnection.InvokeAsync("StartRound");

        await AttendreAsync(() => roundStartedPlayer is not null);
        await AttendreAsync(() => roundStartedHost is not null);

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

        var creation = await _hostConnection.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(
            Tags: [],
            ModeEquipe: false,
            SeriesSetups: [new SeriesSetupDto(NouveauSeriesConfig(1), [RoundMode.Qcm])],
            Config: null));

        await _hostConnection.InvokeAsync("StartRound");
        await AvecTimeout(roundEndedTcs.Task, TimeSpan.FromSeconds(5));

        // Un seul round dans l'unique série : plus rien à démarrer après NextRound().
        await _hostConnection.InvokeAsync("NextRound");

        var exception = await Assert.ThrowsAsync<HubException>(() => _hostConnection.InvokeAsync("StartRound"));
        Assert.Contains("Plus de round classique", exception.Message);
    }

    [Fact]
    public async Task RejouerPartie_ApresFinDePartie_ResetLesScoresEtPermetDeRedemarrer()
    {
        var roundEndedTcs = new TaskCompletionSource<RoundEndedDto>();
        var gameRestartedTcs = new TaskCompletionSource();
        RoundStartedForHostDto? roundStartedApresReplay = null;

        _hostConnection.On<RoundEndedDto>("RoundEnded", payload => roundEndedTcs.TrySetResult(payload));
        _hostConnection.On("GameRestarted", () => gameRestartedTcs.TrySetResult());

        var creation = await _hostConnection.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(
            Tags: [],
            ModeEquipe: false,
            SeriesSetups: [new SeriesSetupDto(NouveauSeriesConfig(1), [RoundMode.Qcm])],
            Config: null));

        await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-1");

        RoundStartedForPlayersDto? roundStartedPlayer = null;
        RoundStartedForHostDto? roundStartedHost = null;
        _playerConnection.On<RoundStartedForPlayersDto>("RoundStarted", payload => roundStartedPlayer = payload);
        _hostConnection.On<RoundStartedForHostDto>("RoundStarted", payload => roundStartedHost = payload);
        await _hostConnection.InvokeAsync("StartRound");
        await AttendreAsync(() => roundStartedPlayer is not null && roundStartedHost is not null);

        var bonneOption = roundStartedPlayer!.QcmOptions!.First(o => o.TrackId == roundStartedHost!.TrackId);
        var resultat = await _playerConnection.InvokeAsync<RoundAnswerResultDto>("SubmitAnswer", new SubmitAnswerRequestDto(bonneOption.TrackId));
        await AvecTimeout(roundEndedTcs.Task, TimeSpan.FromSeconds(5));

        await _hostConnection.InvokeAsync("EndGame");
        await _hostConnection.InvokeAsync("RejouerPartie");
        await AttendreAsync(() => gameRestartedTcs.Task.IsCompleted);

        // Reconnexion avec le même playerId : le score doit être remis à zéro.
        var rejoin = await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-1");
        Assert.Equal(0, rejoin.Score);

        // La partie doit pouvoir redémarrer sans repasser par CreateGame.
        _hostConnection.On<RoundStartedForHostDto>("RoundStarted", payload => roundStartedApresReplay = payload);
        await _hostConnection.InvokeAsync("StartRound");
        await AttendreAsync(() => roundStartedApresReplay is not null);

        Assert.NotNull(roundStartedApresReplay);
        Assert.True(resultat.Points > 0);
    }

    [Fact]
    public async Task ModeEquipe_CreationEtJoinTeam_AgregeLeScoreParEquipe()
    {
        var creation = await _hostConnection.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(
            Tags: [],
            ModeEquipe: true,
            SeriesSetups: [new SeriesSetupDto(NouveauSeriesConfig(1), [RoundMode.Qcm])],
            Config: null,
            NomsEquipes: ["Rouge", "Bleu"]));

        Assert.Equal(2, creation.Teams.Count);
        Assert.Contains(creation.Teams, t => t.Nom == "Rouge");
        var equipeRouge = creation.Teams.First(t => t.Nom == "Rouge");

        var join = await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-1");
        Assert.Equal(2, join.Teams.Count);
        Assert.Null(join.TeamId); // pas encore rejoint d'équipe

        var teamChangedTcs = new TaskCompletionSource<PlayerTeamChangedDto>();
        _hostConnection.On<PlayerTeamChangedDto>("PlayerTeamChanged", payload => teamChangedTcs.TrySetResult(payload));

        await _playerConnection.InvokeAsync("JoinTeam", equipeRouge.Id);
        var teamChanged = await AvecTimeout(teamChangedTcs.Task, TimeSpan.FromSeconds(5));
        Assert.Equal("player-1", teamChanged.PlayerId);
        Assert.Equal(equipeRouge.Id, teamChanged.TeamId);

        RoundStartedForPlayersDto? roundStartedPlayer = null;
        RoundStartedForHostDto? roundStartedHost = null;
        var scoreUpdates = new List<ScoreUpdateDto>();
        _playerConnection.On<RoundStartedForPlayersDto>("RoundStarted", payload => roundStartedPlayer = payload);
        _hostConnection.On<RoundStartedForHostDto>("RoundStarted", payload => roundStartedHost = payload);
        _playerConnection.On<ScoreUpdateDto>("ScoreUpdate", scores => scoreUpdates.Add(scores));

        await _hostConnection.InvokeAsync("StartRound");
        await AttendreAsync(() => roundStartedPlayer is not null && roundStartedHost is not null);

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
        var creation = await _hostConnection.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(
            Tags: [],
            ModeEquipe: false,
            SeriesSetups: [new SeriesSetupDto(NouveauSeriesConfig(1), [RoundMode.Qcm])],
            Config: config));

        await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-1");
        await _hostConnection.InvokeAsync("StartRound");
        await AttendreAsync(() => roundStartedPlayer is not null && roundStartedHost is not null);

        var correctId = roundStartedHost!.TrackId;
        var champAttendu = roundStartedHost.Cible == RoundCible.Titre ? auteursParId[correctId] : titresParId[correctId];

        var distracteurs = roundStartedPlayer!.QcmOptions!.Where(o => o.TrackId != correctId).ToList();
        var champAffiche = roundStartedHost.Cible == RoundCible.Titre
            ? distracteurs.Select(o => o.Title)
            : distracteurs.Select(o => o.Artist);

        Assert.Contains(champAttendu, champAffiche);
    }

    [Fact]
    public async Task JoinGame_RenvoieLeRosterCompletYComprisSoiMeme()
    {
        var creation = await _hostConnection.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(
            Tags: [],
            ModeEquipe: false,
            SeriesSetups: [new SeriesSetupDto(NouveauSeriesConfig(1), [RoundMode.Qcm])],
            Config: null));

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
