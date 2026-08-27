using Blindify.Api.Contracts;
using Blindify.Domain.Configuration;
using Blindify.Domain.Enums;
using Microsoft.AspNetCore.SignalR.Client;

namespace Blindify.Tests.Hubs;

public class GameHubBonusIntegrationTests : IClassFixture<GameHubTestFactory>, IAsyncLifetime
{
    private readonly GameHubTestFactory _factory;
    private HubConnection _hostConnection = null!;
    private HubConnection _alice = null!;
    private HubConnection _bob = null!;

    public GameHubBonusIntegrationTests(GameHubTestFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _hostConnection = _factory.CreateHubConnection();
        _alice = _factory.CreateHubConnection();
        _bob = _factory.CreateHubConnection();
        await _hostConnection.StartAsync();
        await _alice.StartAsync();
        await _bob.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _hostConnection.DisposeAsync();
        await _alice.DisposeAsync();
        await _bob.DisposeAsync();
    }

    private static SeriesConfig NouveauSeriesConfigSansRoundClassique() => new()
    {
        NombreRoundsClassiques = 0,
        DureeFenetreReponseMs = 1000,
        PointsMax = 100,
        PointsMin = 20,
        PenaliteMauvaiseReponseRatio = 0.5,
        PenaliteAbsenceReponse = -5,
        PaliersDeMise = [10, 20, 30, 50],
        DureePhaseMiseMs = 400,
        DureePhaseQuestionMs = 800
    };

    [Fact]
    public async Task QuestionBonus_MiseExpliciteEtPalierParDefaut_ProduitLeResultatAttendu()
    {
        BonusStakeOptionsDto? stakeOptions = null;
        var questionStartedHostTcs = new TaskCompletionSource<BonusQuestionStartedForHostDto>();
        var questionStartedPlayerTcs = new TaskCompletionSource<BonusQuestionStartedForPlayersDto>();
        var bonusResultTcs = new TaskCompletionSource<BonusResultDto>();

        _alice.On<BonusStakeOptionsDto>("BonusStakeOptions", payload => stakeOptions = payload);
        _hostConnection.On<BonusQuestionStartedForHostDto>("BonusQuestionStarted", payload => questionStartedHostTcs.TrySetResult(payload));
        // Cible tirée aléatoirement (Titre/Auteur) depuis le retour utilisateur du 2026-08-24 — on
        // écoute aussi la version joueur pour savoir quoi répondre, plutôt que de supposer Titre.
        _alice.On<BonusQuestionStartedForPlayersDto>("BonusQuestionStarted", payload => questionStartedPlayerTcs.TrySetResult(payload));
        _alice.On<BonusResultDto>("BonusResult", payload => bonusResultTcs.TrySetResult(payload));

        var creation = await _hostConnection.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(ModeEquipe: false));
        await _hostConnection.InvokeAsync(
            "ConfigurerPartie", new ConfigurerPartieRequestDto([new SeriesSetupDto(NouveauSeriesConfigSansRoundClassique(), [], [])], null));

        await _alice.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-alice");
        await _bob.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Bob", "player-bob");

        await _hostConnection.InvokeAsync("StartBonusRound");

        await AttendreAsync(() => stakeOptions is not null);
        Assert.Equal(new[] { 10, 20, 30, 50 }, stakeOptions!.Paliers);

        // Alice choisit explicitement le palier "risqué" (index 3, valeur 50). Bob ne mise pas :
        // il doit recevoir le palier "safe" (index 0, valeur 10) par défaut à l'expiration de la phase mise.
        var miseAcceptee = await _alice.InvokeAsync<bool>("SelectStake", new SelectStakeRequestDto(3));
        Assert.True(miseAcceptee);

        var questionHost = await AvecTimeout(questionStartedHostTcs.Task, TimeSpan.FromSeconds(5));
        Assert.NotNull(questionHost.FilePath);
        var questionPlayer = await AvecTimeout(questionStartedPlayerTcs.Task, TimeSpan.FromSeconds(5));

        // Le morceau bonus est tiré au hasard dans le catalogue de test (t1..t4), et sa cible
        // (Titre/Auteur) l'est aussi (voir BonusRoundService.CreerBonusRound) — on retrouve la bonne
        // réponse par TrackId + Cible plutôt que de les supposer, pour ne pas rendre le test
        // dépendant du tirage.
        var titresConnus = new Dictionary<string, string>
        {
            ["t1"] = "Under the Sea",
            ["t2"] = "Circle of Life",
            ["t3"] = "Let It Go",
            ["t4"] = "Hakuna Matata"
        };
        var artistesConnus = new Dictionary<string, string>
        {
            ["t1"] = "Samuel E. Wright",
            ["t2"] = "Elton John",
            ["t3"] = "Idina Menzel",
            ["t4"] = "Nathan Lane"
        };
        // Mode tiré aléatoirement (Qcm/TapeReponse/PremiereLettre) depuis le retour utilisateur du
        // 2026-08-27 — voir BonusRoundService.CreerBonusRound. En Qcm la bonne réponse est le
        // TrackId (comme en round classique), pas le texte ; en TapeReponse/PremiereLettre le texte
        // complet reste accepté (une première lettre correcte suffit à valider PremiereLettre).
        var bonneReponse = questionPlayer.Mode == RoundMode.Qcm
            ? questionHost.TrackId
            : questionPlayer.Cible == RoundCible.Auteur
                ? artistesConnus[questionHost.TrackId]
                : titresConnus[questionHost.TrackId];

        var reponseAlice = await _alice.InvokeAsync<BonusAnswerResultDto>("SubmitBonusAnswer", new SubmitBonusAnswerRequestDto(bonneReponse));
        Assert.True(reponseAlice.EstCorrecte);
        Assert.Equal(50, reponseAlice.Points);

        // Bob ne répond jamais : à l'expiration de la phase question, il doit perdre sa mise par défaut (10).
        var resultat = await AvecTimeout(bonusResultTcs.Task, TimeSpan.FromSeconds(5));

        var entreeAlice = resultat.Resultats.Single(r => r.PlayerId == "player-alice");
        Assert.True(entreeAlice.EstCorrecte);
        Assert.Equal(50, entreeAlice.Mise);
        Assert.Equal(50, entreeAlice.Points);

        var entreeBob = resultat.Resultats.Single(r => r.PlayerId == "player-bob");
        Assert.False(entreeBob.EstCorrecte);
        Assert.Equal(10, entreeBob.Mise);
        Assert.Equal(-10, entreeBob.Points);
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
