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
