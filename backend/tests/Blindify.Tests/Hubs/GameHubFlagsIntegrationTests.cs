using Blindify.Api.Contracts;
using Blindify.Domain.Configuration;
using Blindify.Domain.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Blindify.Tests.Hubs;

/// <summary>Signalement en direct (V2, section 12.4) — voir GameHub.SignalerMorceau. Une instance de
/// GameHubTestFactory PAR TEST (pas IClassFixture) : les tests d'exclusion ont besoin d'un catalogue
/// (4 morceaux) et d'un flags.json vierges, sans pollution des raisons "bloquantes" posées par les
/// autres tests de ce fichier.</summary>
public class GameHubFlagsIntegrationTests : IAsyncLifetime
{
    private GameHubTestFactory _factory = null!;
    private HubConnection _hostConnection = null!;
    private HubConnection _playerConnection = null!;

    public async Task InitializeAsync()
    {
        _factory = new GameHubTestFactory();
        _hostConnection = _factory.CreateHubConnection();
        _playerConnection = _factory.CreateHubConnection();
        await _hostConnection.StartAsync();
        await _playerConnection.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _hostConnection.DisposeAsync();
        await _playerConnection.DisposeAsync();
        await _factory.DisposeAsync();
    }

    private static async Task<CreateGameResultDto> CreerEtConfigurerPartie(HubConnection host, int nombreRoundsClassiques, GameConfig? config = null)
    {
        var creation = await host.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(false, null));
        await host.InvokeAsync("ConfigurerPartie", new ConfigurerPartieRequestDto(
            NombreSeries: 1,
            NombreRoundsClassiques: nombreRoundsClassiques,
            DureeFenetreReponseMs: 800,
            ThemesVivier: [],
            Config: config,
            DureePhaseMiseMs: 5000,
            DureePhaseQuestionMs: 5000));
        return creation;
    }

    private async Task<string> DemarrerRoundEtObtenirTrackId()
    {
        var tcs = new TaskCompletionSource<RoundStartedForHostDto>();
        using var subscription = _hostConnection.On<RoundStartedForHostDto>("RoundStarted", payload => tcs.TrySetResult(payload));
        await _hostConnection.InvokeAsync("StartRound");
        var roundStarted = await AvecTimeout(tcs.Task, TimeSpan.FromSeconds(5));
        return roundStarted.TrackId;
    }

    [Fact]
    public async Task SignalerMorceau_MorceauJoueDansLaSession_RetourneUnFlagIdEtDiffuseAuHost()
    {
        await CreerEtConfigurerPartie(_hostConnection, 1);
        var trackId = await DemarrerRoundEtObtenirTrackId();

        var morceauSignaleTcs = new TaskCompletionSource<MorceauSignaleDto>();
        _hostConnection.On<MorceauSignaleDto>("MorceauSignale", payload => morceauSignaleTcs.TrySetResult(payload));

        var resultat = await _hostConnection.InvokeAsync<SignalementResultDto>("SignalerMorceau", new SignalementRequestDto(trackId, RaisonSignalement.MauvaiseVersion, "version live"));

        Assert.False(resultat.DejaSignale);
        Assert.NotEmpty(resultat.FlagId);

        var evenement = await AvecTimeout(morceauSignaleTcs.Task, TimeSpan.FromSeconds(5));
        Assert.Equal(trackId, evenement.TrackId);
        Assert.Equal(RaisonSignalement.MauvaiseVersion, evenement.Raison);

        await _hostConnection.InvokeAsync("EndGame");
    }

    [Fact]
    public async Task SignalerMorceau_MemeMorceauMemeRaisonDeuxFois_DejaSignaleVrai()
    {
        await CreerEtConfigurerPartie(_hostConnection, 1);
        var trackId = await DemarrerRoundEtObtenirTrackId();

        var premier = await _hostConnection.InvokeAsync<SignalementResultDto>("SignalerMorceau", new SignalementRequestDto(trackId, RaisonSignalement.AudioDefectueux, null));
        var second = await _hostConnection.InvokeAsync<SignalementResultDto>("SignalerMorceau", new SignalementRequestDto(trackId, RaisonSignalement.AudioDefectueux, "autre commentaire"));

        Assert.False(premier.DejaSignale);
        Assert.True(second.DejaSignale);
        Assert.Equal(premier.FlagId, second.FlagId);

        await _hostConnection.InvokeAsync("EndGame");
    }

    [Fact]
    public async Task SignalerMorceau_MorceauJamaisJoueDansCettePartie_LeveUneException()
    {
        await CreerEtConfigurerPartie(_hostConnection, 1);

        await Assert.ThrowsAsync<HubException>(() =>
            _hostConnection.InvokeAsync<SignalementResultDto>("SignalerMorceau", new SignalementRequestDto("track-jamais-joue", RaisonSignalement.Autre, "commentaire")));

        await _hostConnection.InvokeAsync("EndGame");
    }

    [Fact]
    public async Task SignalerMorceau_AppeleParUnJoueurNonAuthentifie_LeveUneException()
    {
        var creation = await CreerEtConfigurerPartie(_hostConnection, 1);
        await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-1");
        var trackId = await DemarrerRoundEtObtenirTrackId();

        await Assert.ThrowsAsync<HubException>(() =>
            _playerConnection.InvokeAsync<SignalementResultDto>("SignalerMorceau", new SignalementRequestDto(trackId, RaisonSignalement.Autre, "commentaire")));

        await _hostConnection.InvokeAsync("EndGame");
    }

    [Fact]
    public async Task SignalerMorceau_AppeleParUnAdminAuthentifie_EstAccepte()
    {
        var creation = await CreerEtConfigurerPartie(_hostConnection, 1);
        await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-1");
        var trackId = await DemarrerRoundEtObtenirTrackId();

        var auth = await _playerConnection.InvokeAsync<AdminAuthResultDto>("AuthenticateAdmin", "test-admin-pw");
        Assert.True(auth.Success);

        var resultat = await _playerConnection.InvokeAsync<SignalementResultDto>("SignalerMorceau", new SignalementRequestDto(trackId, RaisonSignalement.HorsTheme, null));
        Assert.False(resultat.DejaSignale);

        await _hostConnection.InvokeAsync("EndGame");
    }

    [Fact]
    public async Task SignalerMorceau_NeDiffuseJamaisAuxJoueurs()
    {
        var creation = await CreerEtConfigurerPartie(_hostConnection, 1);
        await _playerConnection.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-1");
        var trackId = await DemarrerRoundEtObtenirTrackId();

        var recuParLeJoueur = false;
        _playerConnection.On<MorceauSignaleDto>("MorceauSignale", _ => recuParLeJoueur = true);

        await _hostConnection.InvokeAsync<SignalementResultDto>("SignalerMorceau", new SignalementRequestDto(trackId, RaisonSignalement.PasSaPlace, null));

        // Pas d'événement à attendre côté joueur (justement) — une courte pause suffit à laisser
        // une éventuelle diffusion erronée arriver avant l'assertion.
        await Task.Delay(200);
        Assert.False(recuParLeJoueur);

        await _hostConnection.InvokeAsync("EndGame");
    }

    [Fact]
    public async Task ConfigurerPartie_MorceauSignaleBloquant_EstExcluDeLaSelectionSuivante()
    {
        await CreerEtConfigurerPartie(_hostConnection, 1);
        var trackIdBloque = await DemarrerRoundEtObtenirTrackId();
        await _hostConnection.InvokeAsync<SignalementResultDto>("SignalerMorceau", new SignalementRequestDto(trackIdBloque, RaisonSignalement.PasSaPlace, null));
        await _hostConnection.InvokeAsync("EndGame");

        // Catalogue de test = 4 morceaux (t1..t4, voir GameHubTestFactory) ; en demander 3 doit donc
        // exclure exactement le morceau bloqué et retenir les 3 autres.
        await CreerEtConfigurerPartie(_hostConnection, 3);
        var tracksSelectionnes = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            tracksSelectionnes.Add(await DemarrerRoundEtObtenirTrackId());
            if (i < 2) await _hostConnection.InvokeAsync("NextRound");
        }

        Assert.DoesNotContain(trackIdBloque, tracksSelectionnes);
        Assert.Equal(3, tracksSelectionnes.Distinct().Count());

        await _hostConnection.InvokeAsync("EndGame");
    }

    [Fact]
    public async Task ConfigurerPartie_ExclureMorceauxSignalesDesactive_ReautoriseLeMorceauBloque()
    {
        await CreerEtConfigurerPartie(_hostConnection, 1);
        var trackIdBloque = await DemarrerRoundEtObtenirTrackId();
        await _hostConnection.InvokeAsync<SignalementResultDto>("SignalerMorceau", new SignalementRequestDto(trackIdBloque, RaisonSignalement.PasSaPlace, null));
        await _hostConnection.InvokeAsync("EndGame");

        // Avec ExclureMorceauxSignales=false, les 4 morceaux du catalogue (dont le bloqué) doivent
        // rester disponibles — sinon demander les 4 échouerait (pas assez de morceaux).
        await CreerEtConfigurerPartie(_hostConnection, 4, new GameConfig { ExclureMorceauxSignales = false });
        var tracksSelectionnes = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            tracksSelectionnes.Add(await DemarrerRoundEtObtenirTrackId());
            if (i < 3) await _hostConnection.InvokeAsync("NextRound");
        }

        Assert.Contains(trackIdBloque, tracksSelectionnes);

        await _hostConnection.InvokeAsync("EndGame");
    }

    private static async Task<T> AvecTimeout<T>(Task<T> task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task) throw new TimeoutException("Condition non remplie dans le délai imparti.");
        return await task;
    }
}
