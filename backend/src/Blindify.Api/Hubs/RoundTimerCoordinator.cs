using System.Collections.Concurrent;
using Blindify.Api.Contracts;
using Blindify.Application.Rounds;
using Blindify.Application.Sessions;
using Blindify.Application.Stats;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;
using Blindify.Infrastructure.Stats;
using Blindify.Infrastructure.Tracks;
using Microsoft.AspNetCore.SignalR;

namespace Blindify.Api.Hubs;

/// <summary>
/// Surveille la fin d'un round classique (durée fixe, indépendante des réponses reçues — architecture.md
/// section 6). Tourne hors du cycle de vie d'un appel de hub, d'où l'usage de IHubContext plutôt que
/// Clients directement.
/// </summary>
public class RoundTimerCoordinator(
    IHubContext<GameHub> hubContext,
    IGameSessionStore sessionStore,
    IRoundService roundService,
    ITracksRepository tracksRepository,
    IStatsRepository statsRepository)
{
    private const int IntervalleVerificationMs = 250;

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _timers = new();

    /// <summary>Dernier round terminé par session (code → Round.Id) — garantit qu'un round n'est
    /// terminé et diffusé qu'une seule fois, que la fin vienne du minuteur ou de TerminerMaintenantAsync
    /// (bouton « Révéler maintenant » du host), même si les deux se produisent au même instant. Lu et
    /// écrit uniquement sous session.Lock.</summary>
    private readonly ConcurrentDictionary<string, Guid> _roundsTermines = new();

    private bool MarquerTermine(string code, Round round)
    {
        if (_roundsTermines.TryGetValue(code, out var dernier) && dernier == round.Id) return false;
        _roundsTermines[code] = round.Id;
        return true;
    }

    public void DemarrerSurveillance(string code, SeriesConfig config)
    {
        Annuler(code);
        // Un même round peut être redémarré (StartRound sans NextRound rejoue le round courant, même
        // Round.Id) : sans ça, il serait considéré comme déjà terminé et ne se terminerait jamais.
        _roundsTermines.TryRemove(code, out _);
        var cts = new CancellationTokenSource();
        _timers[code] = cts;
        _ = SurveillerAsync(code, config, cts.Token);
    }

    public void Annuler(string code)
    {
        if (_timers.TryRemove(code, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    /// <summary>Termine le round courant avant la fin du chrono (GameHub.RevelerMaintenant) : mêmes
    /// effets qu'une fin naturelle (absents pénalisés, stats, RoundEnded + ScoreUpdate). Les gardes
    /// métier (tout le monde a répondu, pas de pause) sont vérifiées par l'appelant sous
    /// session.Lock. Retourne false si le round était déjà terminé ou n'existe plus.</summary>
    public async Task<bool> TerminerMaintenantAsync(GameSession session, SeriesConfig config)
    {
        Round round;
        lock (session.Lock)
        {
            var roundCourant = session.RoundCourant();
            if (roundCourant?.DebutRound is null) return false;
            round = roundCourant;
            if (!MarquerTermine(session.Id, round)) return false;
            Annuler(session.Id);
            roundService.TerminerParTimeout(session, round, config);
        }

        await DiffuserFinDeRoundAsync(session, round);
        return true;
    }

    private async Task SurveillerAsync(string code, SeriesConfig config, CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(IntervalleVerificationMs, token);

                var session = sessionStore.Get(code);
                if (session is null) return;

                bool termine;
                Round round;
                lock (session.Lock)
                {
                    var roundCourant = session.RoundCourant();
                    if (roundCourant?.DebutRound is null) return;
                    round = roundCourant;

                    var pauseEnCoursMs = session.EnPause && session.PauseDemarreeA is not null
                        ? (DateTimeOffset.UtcNow - session.PauseDemarreeA.Value).TotalMilliseconds
                        : 0;
                    var tempsEcouleMs = (DateTimeOffset.UtcNow - round.DebutRound.Value).TotalMilliseconds
                                         - (round.DureeEnPauseMs + pauseEnCoursMs);

                    termine = tempsEcouleMs >= config.DureeFenetreReponseMs;
                    if (termine)
                    {
                        // Déjà terminé par « Révéler maintenant » entre deux vérifications : rien à faire.
                        if (!MarquerTermine(code, round)) return;
                        roundService.TerminerParTimeout(session, round, config);
                    }
                }

                if (termine)
                {
                    await DiffuserFinDeRoundAsync(session, round);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Annulé volontairement (NextRound/EndGame déclenchés avant l'échéance naturelle).
        }
        finally
        {
            _timers.TryRemove(code, out _);
        }
    }

    private async Task DiffuserFinDeRoundAsync(GameSession session, Round round)
    {
        var track = tracksRepository.GetById(round.TrackId);

        // V2 (socle statistiques) — TerminerParTimeout vient de tourner (voir SurveillerAsync
        // ci-dessus), Reponses contient donc déjà les entrées synthétiques EstAbsent. Ne tient pas
        // compte d'un ValidateAnswerManually ultérieur — limitation assumée, voir RoundStatsAggregator.
        statsRepository.EnregistrerResultatsRound(RoundStatsAggregator.PourRoundClassique(round));

        var resultats = session.Players
            .Select(p =>
            {
                var reponse = round.Reponses.FirstOrDefault(r => r.PlayerId == p.PlayerId);
                return new RoundResultEntryDto(p.PlayerId, reponse?.Reponse, reponse?.EstCorrecte, reponse?.Points ?? 0, reponse?.EcartAnnee);
            })
            .ToList();

        var film = track is not null ? FilmNameResolver.Resoudre(track) : "?";

        await hubContext.Clients.Group(session.Id)
            .SendAsync("RoundEnded", new RoundEndedDto(round.TrackId, track?.Title ?? "?", track?.Artist ?? "?", track?.CoverPath, round.Cible, film, resultats, track?.Year));

        await hubContext.Clients.Group(session.Id).SendAsync("ScoreUpdate", ScoreDtoBuilder.Construire(session));
    }
}
