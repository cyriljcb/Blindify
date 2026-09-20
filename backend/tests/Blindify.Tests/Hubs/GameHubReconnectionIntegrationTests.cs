using Blindify.Api.Contracts;
using Blindify.Domain.Configuration;
using Microsoft.AspNetCore.SignalR.Client;

namespace Blindify.Tests.Hubs;

/// <summary>Couvre la partie automatisable des critères d'acceptation V2 de la reconnexion
/// (GameHub.OnConnectedAsync/OnDisconnectedAsync) — le verrouillage/déverrouillage d'un vrai téléphone
/// reste à vérifier manuellement (voir le plan de mise en œuvre V2), mais la ré-association côté serveur,
/// le rejet d'un RoundId périmé et le délai de grâce avant PlayerDisconnected sont testables ici.</summary>
public class GameHubReconnectionIntegrationTests : IClassFixture<GameHubTestFactory>, IAsyncLifetime
{
    private readonly GameHubTestFactory _factory;
    private HubConnection _hostConnection = null!;

    public GameHubReconnectionIntegrationTests(GameHubTestFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _hostConnection = _factory.CreateHubConnection();
        await _hostConnection.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _hostConnection.DisposeAsync();
    }

    private async Task<string> CreerEtConfigurerPartie(int nombreRoundsClassiques = 1, GameConfig? config = null)
    {
        var creation = await _hostConnection.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(ModeEquipe: false));
        await _hostConnection.InvokeAsync("ConfigurerPartie", new ConfigurerPartieRequestDto(
            NombreSeries: 1,
            NombreRoundsClassiques: nombreRoundsClassiques,
            DureeFenetreReponseMs: 5000,
            ThemesVivier: [],
            Config: config,
            DureePhaseMiseMs: 5000,
            DureePhaseQuestionMs: 5000));
        return creation.Code;
    }

    [Fact]
    public async Task OnConnectedAsync_ReconnexionAvecCodeEtPlayerId_RattacheLeJoueurEtRenvoieLetatCourant()
    {
        var code = await CreerEtConfigurerPartie();

        await using var premiereConnexion = _factory.CreateHubConnection();
        await premiereConnexion.StartAsync();
        await premiereConnexion.InvokeAsync<JoinGameResultDto>("JoinGame", code, "Alice", "player-reco-auto-1");

        RoundStartedForPlayersDto? roundStartedPlayer = null;
        premiereConnexion.On<RoundStartedForPlayersDto>("RoundStarted", payload => roundStartedPlayer = payload);
        await _hostConnection.InvokeAsync("StartRound");
        await AttendreAsync(() => roundStartedPlayer is not null);

        var playerReconnectedTcs = new TaskCompletionSource<PlayerConnectionChangedDto>();
        _hostConnection.On<PlayerConnectionChangedDto>("PlayerReconnected", payload => playerReconnectedTcs.TrySetResult(payload));

        // Reconnexion automatique (V2) : nouvelle connexion portant ?code&playerId dans l'URL du hub,
        // SANS rappeler JoinGame — exactement le chemin qu'emprunte le client Flutter après une
        // coupure réseau (voir GameHub.OnConnectedAsync).
        var etatCourantTcs = new TaskCompletionSource<EtatCourantConnexionDto>();
        await using var reconnexionAutomatique = _factory.CreateHubConnection(code, "player-reco-auto-1");
        reconnexionAutomatique.On<EtatCourantConnexionDto>("EtatCourant", payload => etatCourantTcs.TrySetResult(payload));
        await reconnexionAutomatique.StartAsync();

        var etatCourant = await AvecTimeout(etatCourantTcs.Task, TimeSpan.FromSeconds(5));
        Assert.Equal(0, etatCourant.Score);
        Assert.NotNull(etatCourant.EtatCourant);
        Assert.Equal(roundStartedPlayer!.Mode, etatCourant.EtatCourant!.Round!.Mode);

        var playerReconnected = await AvecTimeout(playerReconnectedTcs.Task, TimeSpan.FromSeconds(5));
        Assert.Equal("player-reco-auto-1", playerReconnected.PlayerId);
        Assert.True(playerReconnected.EstConnecte);
    }

    [Fact]
    public async Task OnConnectedAsync_ReconnexionRapidePendantLeDelaiDeGrace_NeDiffusePasPlayerDisconnected()
    {
        // Délai de grâce large par rapport au temps réel que prend une reconnexion en mémoire (tests),
        // pour ne pas rendre le test flaky, mais court par rapport au timeout d'assertion ci-dessous.
        var code = await CreerEtConfigurerPartie(config: new GameConfig { DelaiGraceDeconnexionMs = 1500 });

        var premiereConnexion = _factory.CreateHubConnection();
        await premiereConnexion.StartAsync();
        await premiereConnexion.InvokeAsync<JoinGameResultDto>("JoinGame", code, "Alice", "player-reco-grace-1");

        var playerDisconnectedRecu = false;
        _hostConnection.On<PlayerConnectionChangedDto>("PlayerDisconnected", _ => playerDisconnectedRecu = true);

        await premiereConnexion.DisposeAsync();

        // Reconnexion immédiate (bien avant l'expiration des 1500ms de grâce).
        await using var reconnexionAutomatique = _factory.CreateHubConnection(code, "player-reco-grace-1");
        await reconnexionAutomatique.StartAsync();

        // Attend au-delà du délai de grâce pour laisser le OnDisconnectedAsync de la première connexion
        // effectuer sa revérification tardive.
        await Task.Delay(2000);

        Assert.False(playerDisconnectedRecu, "PlayerDisconnected n'aurait pas dû être diffusé après une reconnexion pendant le délai de grâce.");
    }

    [Fact]
    public async Task OnDisconnectedAsync_SansReconnexionDansLeDelaiDeGrace_DiffusePlayerDisconnected()
    {
        var code = await CreerEtConfigurerPartie(config: new GameConfig { DelaiGraceDeconnexionMs = 150 });

        var premiereConnexion = _factory.CreateHubConnection();
        await premiereConnexion.StartAsync();
        await premiereConnexion.InvokeAsync<JoinGameResultDto>("JoinGame", code, "Alice", "player-reco-grace-2");

        var playerDisconnectedTcs = new TaskCompletionSource<PlayerConnectionChangedDto>();
        _hostConnection.On<PlayerConnectionChangedDto>("PlayerDisconnected", payload => playerDisconnectedTcs.TrySetResult(payload));

        await premiereConnexion.DisposeAsync();

        var playerDisconnected = await AvecTimeout(playerDisconnectedTcs.Task, TimeSpan.FromSeconds(5));
        Assert.Equal("player-reco-grace-2", playerDisconnected.PlayerId);
        Assert.False(playerDisconnected.EstConnecte);
    }

    [Fact]
    public async Task SubmitAnswer_RoundIdPerime_EstIgnoreSilencieusementSansConsommerLessai()
    {
        var code = await CreerEtConfigurerPartie(nombreRoundsClassiques: 2);

        await using var playerConnection = _factory.CreateHubConnection();
        await playerConnection.StartAsync();
        await playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", code, "Alice", "player-roundid-1");

        RoundStartedForPlayersDto? roundStarted = null;
        playerConnection.On<RoundStartedForPlayersDto>("RoundStarted", payload => roundStarted = payload);

        await _hostConnection.InvokeAsync("StartRound");
        await AttendreAsync(() => roundStarted is not null);
        var premierRoundId = roundStarted!.RoundId;

        await _hostConnection.InvokeAsync("NextRound");
        roundStarted = null;
        await _hostConnection.InvokeAsync("StartRound");
        await AttendreAsync(() => roundStarted is not null);
        var deuxiemeRoundId = roundStarted!.RoundId;

        Assert.NotEqual(premierRoundId, deuxiemeRoundId);

        // Soumission avec l'ID du round précédent (arrivée en retard, ex. après une reconnexion) :
        // ignorée silencieusement, comme un "déjà répondu" — jamais d'exception, jamais de points.
        var reponsePerimee = await playerConnection.InvokeAsync<RoundAnswerResultDto>(
            "SubmitAnswer", new SubmitAnswerRequestDto(premierRoundId, "peu importe"));
        Assert.False(reponsePerimee.EstCorrecte);
        Assert.Equal(0, reponsePerimee.Points);
        Assert.Equal(0, reponsePerimee.NouveauScore);

        // L'essai sur le round COURANT reste disponible : la soumission périmée ne l'a pas consommé.
        var reponseCourante = await playerConnection.InvokeAsync<RoundAnswerResultDto>(
            "SubmitAnswer", new SubmitAnswerRequestDto(deuxiemeRoundId, "peu importe"));
        Assert.True(reponseCourante.Points != 0 || reponseCourante.EstCorrecte);
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
