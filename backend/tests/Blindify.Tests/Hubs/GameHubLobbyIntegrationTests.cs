using Blindify.Api.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Blindify.Tests.Hubs;

/// <summary>Retour utilisateur (2026-10-17) — quitter le salon depuis l'app, vote des thèmes par les
/// joueurs, diffusion des mises enregistrées.</summary>
public class GameHubLobbyIntegrationTests : IClassFixture<GameHubTestFactory>, IAsyncLifetime
{
    private readonly GameHubTestFactory _factory;
    private HubConnection _host = null!;
    private HubConnection _alice = null!;
    private HubConnection _bob = null!;

    public GameHubLobbyIntegrationTests(GameHubTestFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _host = _factory.CreateHubConnection();
        _alice = _factory.CreateHubConnection();
        _bob = _factory.CreateHubConnection();
        await _host.StartAsync();
        await _alice.StartAsync();
        await _bob.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        await _alice.DisposeAsync();
        await _bob.DisposeAsync();
    }

    private async Task<string> CreerSalon()
    {
        var creation = await _host.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(false, null));
        await _alice.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "alice");
        await _bob.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Bob", "bob");
        return creation.Code;
    }

    private Task Configurer(int nombreSeries = 1, int rounds = 1, bool utiliserVotes = false) =>
        _host.InvokeAsync("ConfigurerPartie", new ConfigurerPartieRequestDto(
            NombreSeries: nombreSeries, NombreRoundsClassiques: rounds, DureeFenetreReponseMs: 600, ThemesVivier: [], Config: null,
            UtiliserVotes: utiliserVotes));

    [Fact]
    public async Task QuitterPartie_AuLobby_RetireLeJoueurDuRoster()
    {
        PlayerLeftDto? recu = null;
        _host.On<PlayerLeftDto>("PlayerLeft", p => recu = p);
        var code = await CreerSalon();

        await _alice.InvokeAsync("QuitterPartie");
        await AttendreAsync(() => recu is not null);

        Assert.Equal("alice", recu!.PlayerId);
        Assert.True(recu.Retire);
        var join = await _bob.InvokeAsync<JoinGameResultDto>("JoinGame", code, "Bob", "bob");
        Assert.DoesNotContain(join.Joueurs, j => j.PlayerId == "alice");
    }

    [Fact]
    public async Task QuitterPartie_EnCoursDePartie_PasDePenaliteDAbsenceEtRetourPossible()
    {
        var roundEnded = new TaskCompletionSource<RoundEndedDto>();
        _host.On<RoundEndedDto>("RoundEnded", r => roundEnded.TrySetResult(r));
        var code = await CreerSalon();
        await Configurer();
        await _host.InvokeAsync("StartRound");

        await _alice.InvokeAsync("QuitterPartie");
        var fin = await roundEnded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, fin.Resultats.Single(r => r.PlayerId == "alice").Points);
        Assert.True(fin.Resultats.Single(r => r.PlayerId == "bob").Points < 0);

        // Retour avec le même playerId (crash de l'app, ou changement d'avis) : même joueur, plus « parti ».
        var retour = await _alice.InvokeAsync<JoinGameResultDto>("JoinGame", code, "Alice", "alice");
        Assert.True(retour.Success);
        Assert.False(retour.Joueurs.Single(j => j.PlayerId == "alice").APartie);
    }

    [Fact]
    public async Task QuitterPartie_ReconnexionAutomatiqueParUrl_NeRattachePasLeJoueur()
    {
        var code = await CreerSalon();
        await Configurer();
        await _host.InvokeAsync("StartRound");
        await _alice.InvokeAsync("QuitterPartie");

        var etatRecu = false;
        await using var reconnexion = _factory.CreateHubConnection(code, "alice");
        reconnexion.On<EtatCourantConnexionDto>("EtatCourant", _ => etatRecu = true);
        await reconnexion.StartAsync();
        await Task.Delay(300);

        Assert.False(etatRecu);
    }

    [Fact]
    public async Task VoterThemes_DiffuseLeDecompteEtConfigurerPartieUtiliseLesVotes()
    {
        VotesThemesDto? dernier = null;
        VoteThemesOuvertDto? ouvert = null;
        _host.On<VotesThemesDto>("VotesThemesMisAJour", v => dernier = v);
        _alice.On<VoteThemesOuvertDto>("VoteThemesOuvert", v => ouvert = v);
        await CreerSalon();

        await _host.InvokeAsync("OuvrirVoteThemes", new OuvrirVoteThemesRequestDto(2, NombreRoundsMin: 1));
        await AttendreAsync(() => ouvert is not null);
        Assert.Equal(["test-cover"], ouvert!.Themes);

        Assert.False(await _alice.InvokeAsync<bool>("VoterThemes", new List<string> { "inconnu" }));
        Assert.True(await _alice.InvokeAsync<bool>("VoterThemes", new List<string> { "test-cover" }));
        await AttendreAsync(() => dernier?.Votants.Count == 1);
        Assert.Equal(1, dernier!.ThemesDistincts);
        Assert.Null(dernier.ThemesRetenus);

        // Jamais plus de séries jouées que de thèmes votés.
        var refus = await Assert.ThrowsAsync<HubException>(() => Configurer(nombreSeries: 2, utiliserVotes: true));
        Assert.Contains("1 thème", refus.Message);

        await Configurer(nombreSeries: 1, utiliserVotes: true);
        await AttendreAsync(() => dernier is { Ouvert: false });
        Assert.Equal(["test-cover"], dernier!.ThemesRetenus);

        SerieAnnonceeDto? serie = null;
        _host.On<SerieAnnonceeDto>("SerieAnnoncee", s => serie = s);
        await _host.InvokeAsync("AnnoncerSerieCourante");
        await AttendreAsync(() => serie is not null);
        Assert.Equal(["test-cover"], serie!.Tags);

        // Vote fermé : plus de modification possible.
        Assert.False(await _bob.InvokeAsync<bool>("VoterThemes", new List<string> { "test-cover" }));
    }

    [Fact]
    public async Task VoterThemes_PlusDeThemesQueLeQuota_EstRefuse()
    {
        await CreerSalon();
        await _host.InvokeAsync("OuvrirVoteThemes", new OuvrirVoteThemesRequestDto(1));

        var ok = await _alice.InvokeAsync<bool>("VoterThemes", new List<string> { "test-cover", "autre" });

        Assert.False(ok);
    }

    [Fact]
    public async Task JoinGame_PendantUnVoteOuvert_RenvoieLEtatDuVoteEtMesVotes()
    {
        var code = await CreerSalon();
        await _host.InvokeAsync("OuvrirVoteThemes", new OuvrirVoteThemesRequestDto(2, NombreRoundsMin: 1));
        await _alice.InvokeAsync<bool>("VoterThemes", new List<string> { "test-cover" });

        var join = await _alice.InvokeAsync<JoinGameResultDto>("JoinGame", code, "Alice", "alice");

        Assert.NotNull(join.VoteThemes);
        Assert.True(join.VoteThemes!.Ouvert);
        Assert.Equal(["test-cover"], join.VoteThemes.MesVotes);
    }

    [Fact]
    public async Task SelectStake_DiffuseMiseEnregistreeSansLePalier()
    {
        MiseEnregistreeDto? mise = null;
        BonusStakeOptionsDto? options = null;
        var roundEnded = new TaskCompletionSource<RoundEndedDto>();
        _host.On<MiseEnregistreeDto>("MiseEnregistree", m => mise = m);
        _alice.On<BonusStakeOptionsDto>("BonusStakeOptions", o => options = o);
        _host.On<RoundEndedDto>("RoundEnded", r => roundEnded.TrySetResult(r));
        await CreerSalon();
        await Configurer();
        await _host.InvokeAsync("StartRound");
        await roundEnded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _host.InvokeAsync("NextRound");
        await _host.InvokeAsync("StartBonusRound");
        await AttendreAsync(() => options is not null);

        Assert.True(await _alice.InvokeAsync<bool>("SelectStake", new SelectStakeRequestDto(options!.RoundId, 2)));
        await AttendreAsync(() => mise is not null);

        Assert.Equal("alice", mise!.PlayerId);
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
}
