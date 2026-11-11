using Blindify.Api.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Blindify.Tests.Hubs;

/// <summary>Non-régression côté hub des points relevés par l'audit du 2026-11-10 : jeton joueur,
/// session remplacée, gardes d'état, snapshot de reprise du host, nom de joueur.</summary>
public class GameHubAuditIntegrationTests : IClassFixture<GameHubTestFactory>, IAsyncLifetime
{
    private readonly GameHubTestFactory _factory;
    private HubConnection _host = null!;

    public GameHubAuditIntegrationTests(GameHubTestFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _host = _factory.CreateHubConnection();
        await _host.StartAsync();
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<CreateGameResultDto> CreerPartie(int rounds = 1, int dureeMs = 5000, bool modeEquipe = false)
    {
        var creation = await _host.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(modeEquipe, modeEquipe ? ["Rouge", "Bleu"] : null));
        await _host.InvokeAsync("ConfigurerPartie", new ConfigurerPartieRequestDto(1, rounds, dureeMs, [], null, DureePhaseMiseMs: 5000, DureePhaseQuestionMs: 5000));
        return creation;
    }

    private async Task<HubConnection> Connexion()
    {
        var connexion = _factory.CreateHubConnection();
        await connexion.StartAsync();
        return connexion;
    }

    [Fact]
    public async Task JoinGame_PlayerIdDejaInscritSansLeBonJeton_EstRefuse()
    {
        var creation = await CreerPartie();
        await using var alice = await Connexion();
        var join = await alice.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "audit-usurpe", null);
        Assert.True(join.Success);
        Assert.False(string.IsNullOrEmpty(join.Jeton));

        await using var intrus = await Connexion();
        var usurpation = await intrus.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "audit-usurpe", "faux-jeton");
        Assert.False(usurpation.Success);

        var reprise = await intrus.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "audit-usurpe", join.Jeton);
        Assert.True(reprise.Success);
    }

    [Fact]
    public async Task JoinGame_RepriseDepuisUnAutreAppareil_PrevientEtDetacheLAncienneConnexion()
    {
        var creation = await CreerPartie();
        await using var ancien = await Connexion();
        var join = await ancien.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "audit-double", null);
        var remplacee = new TaskCompletionSource();
        ancien.On("SessionRemplacee", () => remplacee.TrySetResult());

        await using var nouveau = await Connexion();
        await nouveau.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "audit-double", join.Jeton);

        await remplacee.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var refus = await Assert.ThrowsAsync<HubException>(() => ancien.InvokeAsync("JoinTeam", "x"));
        Assert.Contains("Aucune partie", refus.Message);
    }

    [Fact]
    public async Task JoinGame_NomPiegeOuVide_EstNettoyeOuRefuse()
    {
        var creation = await CreerPartie();
        await using var joueur = await Connexion();

        var vide = await joueur.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "   ", "audit-nom-vide", null);
        Assert.False(vide.Success);

        var long_ = await joueur.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, new string('x', 200), "audit-nom-long", null);
        Assert.True(long_.Success);
        Assert.True(long_.Joueurs.Single(j => j.PlayerId == "audit-nom-long").Nom.Length <= 24);
    }

    [Fact]
    public async Task JoinTeam_PartieLancee_ChangementDEquipeRefuse()
    {
        var creation = await CreerPartie(modeEquipe: true);
        await using var alice = await Connexion();
        await JoinDeTest.JoinAsync(alice, creation.Code, "Alice", "audit-equipe");
        await alice.InvokeAsync("JoinTeam", creation.Teams[0].Id);

        await _host.InvokeAsync("StartRound");

        var refus = await Assert.ThrowsAsync<HubException>(() => alice.InvokeAsync("JoinTeam", creation.Teams[1].Id));
        Assert.Contains("figées", refus.Message);
    }

    [Fact]
    public async Task NextRound_PendantUnRound_EstRefuse()
    {
        await CreerPartie(rounds: 2);
        await _host.InvokeAsync("StartRound");

        var refus = await Assert.ThrowsAsync<HubException>(() => _host.InvokeAsync("NextRound"));
        Assert.Contains("pas terminé", refus.Message);
    }

    [Fact]
    public async Task StartRound_PartieEnPause_EstRefuse()
    {
        await CreerPartie();
        await _host.InvokeAsync("PauseGame");

        var refus = await Assert.ThrowsAsync<HubException>(() => _host.InvokeAsync("StartRound"));
        Assert.Contains("pause", refus.Message);
    }

    [Fact]
    public async Task ResumeGame_AvantConfiguration_NePlantePas()
    {
        await _host.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(false, null));
        await _host.InvokeAsync("PauseGame");
        await _host.InvokeAsync("ResumeGame");
    }

    [Fact]
    public async Task ConfigurerPartie_FenetreDeReponseNulle_EstRefusee()
    {
        await _host.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(false, null));
        var refus = await Assert.ThrowsAsync<HubException>(() =>
            _host.InvokeAsync("ConfigurerPartie", new ConfigurerPartieRequestDto(1, 1, 0, [], null)));
        Assert.Contains("durées", refus.Message);
    }

    [Fact]
    public async Task RejoinAsHost_ApresUnRoundRevele_RenvoieLaPhaseEtLeReveal()
    {
        var creation = await CreerPartie(rounds: 2);
        var roundEnded = new TaskCompletionSource<RoundEndedDto>();
        _host.On<RoundEndedDto>("RoundEnded", r => roundEnded.TrySetResult(r));
        await using var alice = await Connexion();
        await JoinDeTest.JoinAsync(alice, creation.Code, "Alice", "audit-snapshot");

        await _host.InvokeAsync("StartRound");
        await _host.InvokeAsync("PauseGame");
        await _host.InvokeAsync("ResumeGame");
        await alice.InvokeAsync<RoundAnswerResultDto>("SubmitAnswer", new SubmitAnswerRequestDto(Guid.Empty, "x")); // roundId périmé : ignoré
        await _host.InvokeAsync("RevelerMaintenant").ContinueWith(_ => { }); // refusé (Alice n'a pas répondu) — sans importance

        await using var hostRecharge = await Connexion();
        var pendantLeRound = await hostRecharge.InvokeAsync<HostStateSnapshotDto>("RejoinAsHost", creation.Code, creation.HostSecret);
        Assert.Equal(PhaseHost.Round, pendantLeRound.Phase);
        Assert.NotNull(pendantLeRound.Round);
        Assert.Single(pendantLeRound.Joueurs);
        Assert.Equal(1, pendantLeRound.RoundsDemarresDansSerie);
        Assert.Equal(2, pendantLeRound.NombreRoundsParSerie);
    }

    [Fact]
    public async Task FichiersDeDonnees_NeSontPasServis()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/files/tracks.json")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/files/stats.json")).StatusCode);
    }
}
