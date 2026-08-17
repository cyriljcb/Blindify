using System.Collections.Concurrent;
using Blindify.Api.Contracts;
using Blindify.Application.Bonus;
using Blindify.Application.Rounds;
using Blindify.Application.Sessions;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;
using Blindify.Infrastructure.Tracks;
using Microsoft.AspNetCore.SignalR;

namespace Blindify.Api.Hubs;

/// <summary>
/// Enchaîne les deux phases de la question bonus (mise à l'aveugle puis question, sans dégressivité) —
/// voir architecture.md section 7. Même logique de polling que RoundTimerCoordinator.
/// </summary>
public class BonusTimerCoordinator(
    IHubContext<GameHub> hubContext,
    IGameSessionStore sessionStore,
    IBonusRoundService bonusRoundService,
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
            if (!await AttendreFinPhaseAsync(code, config.DureePhaseMiseMs, br => br.DebutPhaseMise, token)) return;

            var session = sessionStore.Get(code);
            var bonusRound = session?.SerieCourante().BonusRound;
            if (session is null || bonusRound is null) return;

            bonusRoundService.AppliquerPaliersParDefaut(session, bonusRound);
            bonusRoundService.DemarrerPhaseQuestion(bonusRound, DateTimeOffset.UtcNow);
            await DiffuserDebutPhaseQuestionAsync(session, bonusRound, config);

            if (!await AttendreFinPhaseAsync(code, config.DureePhaseQuestionMs, br => br.DebutPhaseQuestion, token)) return;

            session = sessionStore.Get(code);
            bonusRound = session?.SerieCourante().BonusRound;
            if (session is null || bonusRound is null) return;

            bonusRoundService.TerminerParTimeout(session, bonusRound, config);
            await DiffuserResultatAsync(session, bonusRound);
        }
        catch (OperationCanceledException)
        {
            // Annulé volontairement (host a interrompu la partie avant l'échéance naturelle).
        }
        finally
        {
            _timers.TryRemove(code, out _);
        }
    }

    private async Task<bool> AttendreFinPhaseAsync(string code, int dureeMs, Func<BonusRound, DateTimeOffset?> debutSelector, CancellationToken token)
    {
        while (true)
        {
            await Task.Delay(IntervalleVerificationMs, token);

            var session = sessionStore.Get(code);
            var bonusRound = session?.SerieCourante().BonusRound;
            var debut = bonusRound is null ? null : debutSelector(bonusRound);
            if (session is null || bonusRound is null || debut is null) return false;

            var pauseEnCoursMs = session.EnPause && session.PauseDemarreeA is not null
                ? (DateTimeOffset.UtcNow - session.PauseDemarreeA.Value).TotalMilliseconds
                : 0;
            var tempsEcouleMs = (DateTimeOffset.UtcNow - debut.Value).TotalMilliseconds - (bonusRound.DureeEnPauseMs + pauseEnCoursMs);

            if (tempsEcouleMs >= dureeMs) return true;
        }
    }

    private async Task DiffuserDebutPhaseQuestionAsync(GameSession session, BonusRound bonusRound, SeriesConfig config)
    {
        var track = tracksRepository.GetById(bonusRound.TrackId);
        if (track is null) return;

        if (session.HostConnectionId is not null)
        {
            await hubContext.Clients.Client(session.HostConnectionId).SendAsync("BonusQuestionStarted",
                new BonusQuestionStartedForHostDto(track.Id, track.FilePath, track.RefrainStartMs, config.DureePhaseQuestionMs, session.Config.RalentissementBonusActive, session.Config.FacteurRalentissementBonus));
        }

        var joueursConnectes = session.Players.Where(p => p.ConnectionId is not null).Select(p => p.ConnectionId!).ToList();
        await hubContext.Clients.Clients(joueursConnectes).SendAsync("BonusQuestionStarted", new BonusQuestionStartedForPlayersDto(config.DureePhaseQuestionMs));
    }

    private async Task DiffuserResultatAsync(GameSession session, BonusRound bonusRound)
    {
        var track = tracksRepository.GetById(bonusRound.TrackId);

        var resultats = bonusRound.Reponses
            .Select(r => new BonusResultEntryDto(r.PlayerId, Math.Abs(r.Points), r.Reponse, r.EstCorrecte, r.Points))
            .ToList();

        await hubContext.Clients.Group(session.Id)
            .SendAsync("BonusResult", new BonusResultDto(bonusRound.TrackId, track?.Title ?? "?", track?.Artist ?? "?", track?.CoverPath, resultats));

        await hubContext.Clients.Group(session.Id).SendAsync("ScoreUpdate", ScoreDtoBuilder.Construire(session));
    }
}
