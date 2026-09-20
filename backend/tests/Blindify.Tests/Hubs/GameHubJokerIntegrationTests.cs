using System.Net;
using Blindify.Api.Contracts;
using Blindify.Domain.Configuration;
using Blindify.Domain.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Blindify.Tests.Hubs;

/// <summary>Joker (V2, section 12.7) — voir GameHub.UtiliserJoker et Program.cs "/api/joker/cover/{jeton}".</summary>
public class GameHubJokerIntegrationTests : IClassFixture<GameHubTestFactory>, IAsyncLifetime
{
    private readonly GameHubTestFactory _factory;
    private HubConnection _hostConnection = null!;
    private HubConnection _playerConnection = null!;

    public GameHubJokerIntegrationTests(GameHubTestFactory factory)
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

    private static async Task<CreateGameResultDto> CreerEtConfigurerPartie(HubConnection host, List<string> themesVivier, GameConfig? config = null)
    {
        var creation = await host.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(false, null));
        await host.InvokeAsync("ConfigurerPartie", new ConfigurerPartieRequestDto(
            NombreSeries: 1,
            NombreRoundsClassiques: 1,
            DureeFenetreReponseMs: 800,
            ThemesVivier: themesVivier,
            Config: config,
            DureePhaseMiseMs: 5000,
            DureePhaseQuestionMs: 5000));
        return creation;
    }

    [Fact]
    public async Task UtiliserJoker_ModeQcm_RetireDeuxOptionsJamaisLaBonne()
    {
        RoundStartedForHostDto? roundStartedHost = null;
        using var s1 = _hostConnection.On<RoundStartedForHostDto>("RoundStarted", p => roundStartedHost = p);

        // Le morceau ET le mode sont tirés aléatoirement (catalogue de test t1..t5) — on relance
        // jusqu'à Mode.Qcm, même stratégie que les tests existants de ce type (voir
        // GameHubIntegrationTests.QcmFeinteChamp_...).
        for (var tentative = 0; tentative < 50; tentative++)
        {
            roundStartedHost = null;
            var creation = await CreerEtConfigurerPartie(_hostConnection, []);
            await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", $"joker-qcm-{tentative}");
            await _hostConnection.InvokeAsync("StartRound");
            await AttendreAsync(() => roundStartedHost is not null);
            if (roundStartedHost!.Mode == RoundMode.Qcm) break;
            await _hostConnection.InvokeAsync("EndGame");
        }

        Assert.Equal(RoundMode.Qcm, roundStartedHost!.Mode);
        var indice = await _playerConnection.InvokeAsync<JokerIndiceDto>("UtiliserJoker", roundStartedHost.RoundId);

        Assert.NotNull(indice.OptionsRetirees);
        Assert.Equal(2, indice.OptionsRetirees!.Count);
        Assert.DoesNotContain(roundStartedHost.TrackId, indice.OptionsRetirees);
        Assert.Null(indice.CoverUrl);

        await _hostConnection.InvokeAsync("EndGame");
    }

    [Fact]
    public async Task UtiliserJoker_DeuxiemeAppelDuMemeJoueur_LeveHubException()
    {
        var creation = await CreerEtConfigurerPartie(_hostConnection, []);
        await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Bob", "joker-deuxieme");

        RoundStartedForHostDto? roundStartedHost = null;
        using var s1 = _hostConnection.On<RoundStartedForHostDto>("RoundStarted", p => roundStartedHost = p);
        await _hostConnection.InvokeAsync("StartRound");
        await AttendreAsync(() => roundStartedHost is not null);

        await _playerConnection.InvokeAsync<JokerIndiceDto>("UtiliserJoker", roundStartedHost!.RoundId);
        await Assert.ThrowsAsync<HubException>(() => _playerConnection.InvokeAsync<JokerIndiceDto>("UtiliserJoker", roundStartedHost.RoundId));

        await _hostConnection.InvokeAsync("EndGame");
    }

    [Fact]
    public async Task UtiliserJoker_DiffuseEvenementJokerUtiliseATousLesClientsDuGroupe()
    {
        var creation = await CreerEtConfigurerPartie(_hostConnection, []);
        await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Carla", "joker-broadcast");

        RoundStartedForHostDto? roundStartedHost = null;
        using var s1 = _hostConnection.On<RoundStartedForHostDto>("RoundStarted", p => roundStartedHost = p);
        await _hostConnection.InvokeAsync("StartRound");
        await AttendreAsync(() => roundStartedHost is not null);

        var evenementTcs = new TaskCompletionSource<JokerUtiliseDto>();
        using var s2 = _hostConnection.On<JokerUtiliseDto>("JokerUtilise", p => evenementTcs.TrySetResult(p));

        await _playerConnection.InvokeAsync<JokerIndiceDto>("UtiliserJoker", roundStartedHost!.RoundId);

        var evenement = await AvecTimeout(evenementTcs.Task, TimeSpan.FromSeconds(5));
        Assert.Equal("joker-broadcast", evenement.PlayerId);

        await _hostConnection.InvokeAsync("EndGame");
    }

    [Fact]
    public async Task UtiliserJoker_TapeReponseTitreOuAuteur_CoverEndpointRetourneUneImageJpeg()
    {
        RoundStartedForHostDto? roundStartedHost = null;
        using var s1 = _hostConnection.On<RoundStartedForHostDto>("RoundStarted", p => roundStartedHost = p);

        // ThemesVivier=["test-cover"] ne matche que t5 (seul morceau non "disney" du catalogue de
        // test, voir GameHubTestFactory) — élimine le tirage du morceau, il ne reste que le mode à
        // relancer jusqu'à TapeReponse (cible alors forcément Titre/Auteur, t5 n'a pas d'année connue).
        for (var tentative = 0; tentative < 50; tentative++)
        {
            roundStartedHost = null;
            var creation = await CreerEtConfigurerPartie(_hostConnection, ["test-cover"]);
            await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Dan", $"joker-cover-{tentative}");
            await _hostConnection.InvokeAsync("StartRound");
            await AttendreAsync(() => roundStartedHost is not null);
            if (roundStartedHost!.Mode == RoundMode.TapeReponse) break;
            await _hostConnection.InvokeAsync("EndGame");
        }

        Assert.Equal(RoundMode.TapeReponse, roundStartedHost!.Mode);
        Assert.Equal("t5", roundStartedHost.TrackId);

        var indice = await _playerConnection.InvokeAsync<JokerIndiceDto>("UtiliserJoker", roundStartedHost.RoundId);
        Assert.NotNull(indice.CoverUrl);
        Assert.NotNull(indice.Structure);

        using var httpClient = _factory.CreateClient();
        var reponse = await httpClient.GetAsync(indice.CoverUrl);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal("image/jpeg", reponse.Content.Headers.ContentType?.MediaType);

        await _hostConnection.InvokeAsync("EndGame");
    }

    [Fact]
    public async Task GetJokerCover_JetonInconnu_Retourne404()
    {
        using var httpClient = _factory.CreateClient();
        var reponse = await httpClient.GetAsync("/api/joker/cover/jeton-invalide");

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    [Fact]
    public async Task Reconnexion_RejoueLeMemeIndiceQueLePremierUtiliserJoker()
    {
        var creation = await CreerEtConfigurerPartie(_hostConnection, []);
        var playerId = "joker-reco-" + Guid.NewGuid().ToString("N")[..6];
        await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Eve", playerId);

        RoundStartedForHostDto? roundStartedHost = null;
        using var s1 = _hostConnection.On<RoundStartedForHostDto>("RoundStarted", p => roundStartedHost = p);
        await _hostConnection.InvokeAsync("StartRound");
        await AttendreAsync(() => roundStartedHost is not null);

        var premierIndice = await _playerConnection.InvokeAsync<JokerIndiceDto>("UtiliserJoker", roundStartedHost!.RoundId);

        // Nouvelle connexion avec ?code&playerId (reconnexion automatique, V2) — voir GameHub.OnConnectedAsync.
        await using var reconnexion = _factory.CreateHubConnection(creation.Code, playerId);
        var etatCourantTcs = new TaskCompletionSource<EtatCourantConnexionDto>();
        reconnexion.On<EtatCourantConnexionDto>("EtatCourant", p => etatCourantTcs.TrySetResult(p));
        await reconnexion.StartAsync();

        var etatCourant = await AvecTimeout(etatCourantTcs.Task, TimeSpan.FromSeconds(5));

        Assert.False(etatCourant.JokerDisponible);
        var indiceRejoue = etatCourant.EtatCourant?.Round?.JokerIndice;
        Assert.NotNull(indiceRejoue);
        Assert.Equal(premierIndice.OptionsRetirees, indiceRejoue!.OptionsRetirees);
        Assert.Equal(premierIndice.TuilesRestantes, indiceRejoue.TuilesRestantes);
        Assert.Equal(premierIndice.Structure, indiceRejoue.Structure);
        Assert.Equal(premierIndice.Decennie, indiceRejoue.Decennie);
        Assert.Equal(premierIndice.CoverUrl, indiceRejoue.CoverUrl);

        await _hostConnection.InvokeAsync("EndGame");
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
        if (completed != task) throw new TimeoutException("Condition non remplie dans le délai imparti.");
        return await task;
    }
}
