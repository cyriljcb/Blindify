using System.Collections.Concurrent;
using Blindify.Api.Contracts;
using Blindify.Application.Rounds;
using Blindify.Application.Sessions;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;
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
    ITracksRepository tracksRepository)
{
    private const int IntervalleVerificationMs = 250;

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _timers = new();

    public void DemarrerSurveillance(string code, SeriesConfig config)
    {
        Annuler(code);
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

    private async Task SurveillerAsync(string code, SeriesConfig config, CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(IntervalleVerificationMs, token);

                var session = sessionStore.Get(code);
                if (session is null) return;

                var round = session.RoundCourant();
                if (round?.DebutRound is null) return;

                var pauseEnCoursMs = session.EnPause && session.PauseDemarreeA is not null
                    ? (DateTimeOffset.UtcNow - session.PauseDemarreeA.Value).TotalMilliseconds
                    : 0;
                var tempsEcouleMs = (DateTimeOffset.UtcNow - round.DebutRound.Value).TotalMilliseconds
                                     - (round.DureeEnPauseMs + pauseEnCoursMs);

                if (tempsEcouleMs >= config.DureeFenetreReponseMs)
                {
                    roundService.TerminerParTimeout(session, round, config);
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

        var resultats = session.Players
            .Select(p =>
            {
                var reponse = round.Reponses.FirstOrDefault(r => r.PlayerId == p.PlayerId);
                return new RoundResultEntryDto(p.PlayerId, reponse?.Reponse, reponse?.EstCorrecte, reponse?.Points ?? 0);
            })
            .ToList();

        await hubContext.Clients.Group(session.Id)
            .SendAsync("RoundEnded", new RoundEndedDto(round.TrackId, track?.Title ?? "?", track?.Artist ?? "?", track?.CoverPath, resultats));

        await hubContext.Clients.Group(session.Id).SendAsync("ScoreUpdate", ScoreDtoBuilder.Construire(session));
    }
}
