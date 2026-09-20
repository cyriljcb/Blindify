using System.Collections.Concurrent;
using Blindify.Api.Contracts;
using Blindify.Application.Bonus;
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
/// Enchaîne les deux phases de la question bonus (mise à l'aveugle puis question, sans dégressivité) —
/// voir architecture.md section 7. Même logique de polling que RoundTimerCoordinator.
/// </summary>
public class BonusTimerCoordinator(
    IHubContext<GameHub> hubContext,
    IGameSessionStore sessionStore,
    IBonusRoundService bonusRoundService,
    ITracksRepository tracksRepository,
    IStatsRepository statsRepository)
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

            lock (session.Lock)
            {
                bonusRoundService.AppliquerPaliersParDefaut(session, bonusRound);
                bonusRoundService.DemarrerPhaseQuestion(bonusRound, DateTimeOffset.UtcNow);
            }
            await DiffuserDebutPhaseQuestionAsync(session, bonusRound, config);

            // finAnticipee : en mode course, la phase se termine dès qu'un joueur a répondu (juste ou
            // faux) plutôt que d'attendre la durée complète — voir BonusRoundService.SoumettreReponse.
            if (!await AttendreFinPhaseAsync(code, config.DureePhaseQuestionMs, br => br.DebutPhaseQuestion, token, br => br.EstCourse && br.Reponses.Count > 0)) return;

            session = sessionStore.Get(code);
            bonusRound = session?.SerieCourante().BonusRound;
            if (session is null || bonusRound is null) return;

            lock (session.Lock) { bonusRoundService.TerminerParTimeout(session, bonusRound, config); }
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

    private async Task<bool> AttendreFinPhaseAsync(string code, int dureeMs, Func<BonusRound, DateTimeOffset?> debutSelector, CancellationToken token, Func<BonusRound, bool>? finAnticipee = null)
    {
        while (true)
        {
            await Task.Delay(IntervalleVerificationMs, token);

            var session = sessionStore.Get(code);
            var bonusRound = session?.SerieCourante().BonusRound;
            var debut = bonusRound is null ? null : debutSelector(bonusRound);
            if (session is null || bonusRound is null || debut is null) return false;

            if (finAnticipee is not null && finAnticipee(bonusRound)) return true;

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

        // Même construction que GameHub.StartRound pour les rounds classiques — Mode Qcm tiré au
        // hasard côté BonusRoundService.CreerBonusRound, feintes appliquées ici au moment de la
        // diffusion (retour utilisateur 2026-08-27 : QCM/Première lettre aussi en question bonus).
        var (qcmOptions, roundOptions) = GameHub.ConstruireQcmOptions(bonusRound.QcmOptionTrackIds, track, bonusRound.Cible, session.Config, tracksRepository);
        bonusRound.Options = roundOptions;
        var anneeOptions = bonusRound.AnneeOptions?.Select(a => a.ToString()).ToList();

        if (session.HostConnectionId is not null)
        {
            await hubContext.Clients.Client(session.HostConnectionId).SendAsync("BonusQuestionStarted",
                new BonusQuestionStartedForHostDto(track.Id, track.FilePath, track.RefrainStartMs, bonusRound.Id, config.DureePhaseQuestionMs, session.Config.RalentissementBonusActive, session.Config.FacteurRalentissementBonus, bonusRound.Mode, qcmOptions, bonusRound.EstCourse, anneeOptions));
        }

        var joueursConnectes = session.Players.Where(p => p.ConnectionId is not null).Select(p => p.ConnectionId!).ToList();
        await hubContext.Clients.Clients(joueursConnectes).SendAsync("BonusQuestionStarted",
            new BonusQuestionStartedForPlayersDto(bonusRound.Id, config.DureePhaseQuestionMs, bonusRound.Cible, session.SerieCourante().Index, bonusRound.Mode, qcmOptions, bonusRound.EstCourse, TempsEcouleMs: 0, AnneeOptions: anneeOptions));
    }

    private async Task DiffuserResultatAsync(GameSession session, BonusRound bonusRound)
    {
        var track = tracksRepository.GetById(bonusRound.TrackId);

        // V2 (socle statistiques) — après TerminerParTimeout (appelé par l'appelant juste avant),
        // donc Reponses contient déjà les entrées synthétiques EstAbsent pour les non-répondants.
        // Ne tient pas compte d'un ValidateAnswerManually ultérieur (round classique uniquement,
        // pas applicable au bonus de toute façon) — limitation assumée, voir RoundStatsAggregator.
        statsRepository.EnregistrerResultatsRound(RoundStatsAggregator.PourBonus(bonusRound));

        var resultats = bonusRound.Reponses
            .Select(r => new BonusResultEntryDto(r.PlayerId, Math.Abs(r.Points), r.Reponse, r.EstCorrecte, r.Points, r.EcartAnnee))
            .ToList();
        var film = track is not null ? FilmNameResolver.Resoudre(track) : "?";

        await hubContext.Clients.Group(session.Id)
            .SendAsync("BonusResult", new BonusResultDto(bonusRound.TrackId, track?.Title ?? "?", track?.Artist ?? "?", track?.CoverPath, bonusRound.Cible, film, resultats, bonusRound.EstCourse, track?.Year));

        await hubContext.Clients.Group(session.Id).SendAsync("ScoreUpdate", ScoreDtoBuilder.Construire(session));
    }
}
