using Blindify.Api.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Blindify.Tests.Hubs;

/// <summary>Refonte UI (lot 3) — configurations enregistrées (data/presets.json), télécommande
/// relayée à la page host, option « écart avec le joueur devant ».</summary>
public class GameHubRegieIntegrationTests : IClassFixture<GameHubTestFactory>, IAsyncLifetime
{
    private readonly GameHubTestFactory _factory;
    private HubConnection _host = null!;
    private HubConnection _joueur = null!;

    public GameHubRegieIntegrationTests(GameHubTestFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _host = _factory.CreateHubConnection();
        _joueur = _factory.CreateHubConnection();
        await _host.StartAsync();
        await _joueur.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        await _joueur.DisposeAsync();
    }

    private async Task<string> CreerPartie(bool afficherEcart = true)
    {
        var creation = await _host.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(false, null));
        await _host.InvokeAsync("ConfigurerPartie", new ConfigurerPartieRequestDto(
            NombreSeries: 1, NombreRoundsClassiques: 1, DureeFenetreReponseMs: 800, ThemesVivier: [], Config: null,
            AfficherEcart: afficherEcart));
        await _joueur.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-1");
        return creation.Code;
    }

    private static PresetDto Preset(string nom, int rounds = 10) =>
        new(nom, 3, rounds, 15000, ["rock"], MusiqueContinue: true, DelaiEnchainementMs: 10000, AfficherEcart: false);

    [Fact]
    public async Task EnregistrerPreset_MemeNomSansTenirCompteDeLaCasse_RemplaceLaConfiguration()
    {
        await CreerPartie();

        await _host.InvokeAsync<List<PresetDto>>("EnregistrerPreset", Preset("Soirée famille", rounds: 8));
        var liste = await _host.InvokeAsync<List<PresetDto>>("EnregistrerPreset", Preset("soirée FAMILLE", rounds: 12));

        var preset = Assert.Single(liste, p => p.Nom.Equals("soirée famille", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(12, preset.NombreRoundsClassiques);
        Assert.Equal(["rock"], preset.ThemesVivier);
        Assert.False(preset.AfficherEcart);

        var apresSuppression = await _host.InvokeAsync<List<PresetDto>>("SupprimerPreset", "Soirée famille");
        Assert.DoesNotContain(apresSuppression, p => p.Nom.Equals("soirée famille", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Presets_UnJoueurNonAdmin_NePeutNiLesLireNiLesModifier()
    {
        await CreerPartie();

        await Assert.ThrowsAsync<HubException>(() => _joueur.InvokeAsync<List<PresetDto>>("ListerPresets"));
        await Assert.ThrowsAsync<HubException>(() => _joueur.InvokeAsync<List<PresetDto>>("EnregistrerPreset", Preset("Pirate")));
    }

    [Fact]
    public async Task EnregistrerPreset_NomVide_EstRefuse()
    {
        await CreerPartie();

        await Assert.ThrowsAsync<HubException>(() => _host.InvokeAsync<List<PresetDto>>("EnregistrerPreset", Preset("   ")));
    }

    [Fact]
    public async Task EnvoyerCommandeHost_ParUnAdmin_EstRelayeeALaPageHost()
    {
        CommandeHostDto? recue = null;
        _host.On<CommandeHostDto>("CommandeHost", c => recue = c);
        await CreerPartie();
        await _joueur.InvokeAsync<AdminAuthResultDto>("AuthenticateAdmin", "test-admin-pw");

        await _joueur.InvokeAsync("EnvoyerCommandeHost", "suivant");
        await AttendreAsync(() => recue is not null);

        Assert.Equal("suivant", recue!.Commande);
        await Assert.ThrowsAsync<HubException>(() => _joueur.InvokeAsync("EnvoyerCommandeHost", "fermer-salon"));
    }

    [Fact]
    public async Task EnvoyerCommandeHost_ParUnJoueurNonAdmin_EstRefusee()
    {
        await CreerPartie();

        await Assert.ThrowsAsync<HubException>(() => _joueur.InvokeAsync("EnvoyerCommandeHost", "suivant"));
    }

    [Fact]
    public async Task PublierEtatRegie_EstRelayeAuxAdmins_EtRenvoyeALAuthentification()
    {
        await CreerPartie();
        await _host.InvokeAsync("PublierEtatRegie", new EtatRegieDto("Round suivant", true));

        // Un admin qui s'authentifie après coup reçoit tout de suite le dernier état publié.
        EtatRegieDto? recu = null;
        _joueur.On<EtatRegieDto>("EtatRegie", e => recu = e);
        await _joueur.InvokeAsync<AdminAuthResultDto>("AuthenticateAdmin", "test-admin-pw");
        await AttendreAsync(() => recu is not null);
        Assert.Equal("Round suivant", recu!.LibelleAction);

        recu = null;
        await _host.InvokeAsync("PublierEtatRegie", new EtatRegieDto("Révéler maintenant", false));
        await AttendreAsync(() => recu is not null);
        Assert.Equal("Révéler maintenant", recu!.LibelleAction);
        Assert.False(recu.ActionDisponible);
    }

    [Fact]
    public async Task AfficherEcart_EstTransmisAuJoinEtALAnnonceDeSerie()
    {
        SerieAnnonceeDto? annonce = null;
        _joueur.On<SerieAnnonceeDto>("SerieAnnoncee", a => annonce = a);

        var creation = await _host.InvokeAsync<CreateGameResultDto>("CreateGame", new CreateGameRequestDto(false, null));
        await _host.InvokeAsync("ConfigurerPartie", new ConfigurerPartieRequestDto(
            NombreSeries: 1, NombreRoundsClassiques: 1, DureeFenetreReponseMs: 800, ThemesVivier: [], Config: null, AfficherEcart: false));
        var join = await _joueur.InvokeAsync<JoinGameResultDto>("JoinGame", creation.Code, "Alice", "player-1");
        Assert.False(join.AfficherEcart);

        await _host.InvokeAsync("AnnoncerSerieCourante");
        await AttendreAsync(() => annonce is not null);
        Assert.False(annonce!.AfficherEcart);
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
