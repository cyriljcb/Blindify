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
/// voir architecture.md section 7. Même logique de polling que RoundTimerCoordinator, même filet de
/// sécurité (audit 2026-11-10 : une exception tuait la tâche en silence et figeait la question bonus).
/// </summary>
public class BonusTimerCoordinator(
    IHubContext<GameHub> hubContext,
    IGameSessionStore sessionStore,
    IBonusRoundService bonusRoundService,
    ITracksRepository tracksRepository,
    IStatsRepository statsRepository,
    ILogger<BonusTimerCoordinator> logger)
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
            if (session is null) return;

            DebutQuestion? debut;
            lock (session.Lock)
            {
                var bonusRound = BonusCourant(session);
                if (bonusRound is null) return;
                bonusRoundService.AppliquerPaliersParDefaut(session, bonusRound);
                // Options QCM construites AVANT d'ouvrir la phase question (audit 2026-11-10) : un
                // joueur qui se reconnectait dans l'intervalle recevait un QCM sans options.
                debut = PreparerQuestion(session, bonusRound, config);
                bonusRoundService.DemarrerPhaseQuestion(bonusRound, DateTimeOffset.UtcNow);
            }
            if (debut is not null) await DiffuserDebutPhaseQuestionAsync(session, debut);

            // finAnticipee : en mode course, la phase se termine dès qu'un joueur a répondu (juste ou
            // faux) plutôt que d'attendre la durée complète — voir BonusRoundService.SoumettreReponse.
            if (!await AttendreFinPhaseAsync(code, config.DureePhaseQuestionMs, br => br.DebutPhaseQuestion, token, br => br.EstCourse && br.Reponses.Count > 0)) return;

            session = sessionStore.Get(code);
            if (session is null) return;

            BonusResultDto resultat;
            ScoreUpdateDto scores;
            Blindify.Domain.Statistics.RoundStatsUpdate stats;
            lock (session.Lock)
            {
                var bonusRound = BonusCourant(session);
                if (bonusRound is null || bonusRound.TermineA is not null) return;
                bonusRoundService.TerminerParTimeout(session, bonusRound, config);
                session.DerniereActiviteA = DateTimeOffset.UtcNow;
                stats = RoundStatsAggregator.PourBonus(bonusRound);
                resultat = RevealDtoBuilder.BonusResult(bonusRound, tracksRepository);
                scores = ScoreDtoBuilder.Construire(session);
            }

            await hubContext.Clients.Group(session.Id).SendAsync("BonusResult", resultat);
            await hubContext.Clients.Group(session.Id).SendAsync("ScoreUpdate", scores);

            try { statsRepository.EnregistrerResultatsRound(stats); }
            catch (Exception ex) { logger.LogError(ex, "Écriture des statistiques du bonus impossible — partie {Code}", code); }
        }
        catch (OperationCanceledException)
        {
            // Annulé volontairement (host a interrompu la partie avant l'échéance naturelle).
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chrono de la question bonus interrompu par une erreur — partie {Code}", code);
        }
        finally
        {
            _timers.TryRemove(code, out _);
        }
    }

    private static BonusRound? BonusCourant(GameSession session) =>
        session.SeriesList.Count == 0 ? null : session.SerieCourante().BonusRound;

    private async Task<bool> AttendreFinPhaseAsync(string code, int dureeMs, Func<BonusRound, DateTimeOffset?> debutSelector, CancellationToken token, Func<BonusRound, bool>? finAnticipee = null)
    {
        while (true)
        {
            await Task.Delay(IntervalleVerificationMs, token);

            var session = sessionStore.Get(code);
            if (session is null) return false;

            lock (session.Lock)
            {
                var bonusRound = BonusCourant(session);
                var debut = bonusRound is null ? null : debutSelector(bonusRound);
                if (bonusRound is null || debut is null || bonusRound.TermineA is not null) return false;

                if (finAnticipee is not null && finAnticipee(bonusRound)) return true;

                var pauseEnCoursMs = session.EnPause && session.PauseDemarreeA is not null
                    ? (DateTimeOffset.UtcNow - session.PauseDemarreeA.Value).TotalMilliseconds
                    : 0;
                var tempsEcouleMs = (DateTimeOffset.UtcNow - debut.Value).TotalMilliseconds - (bonusRound.DureeEnPauseMs + pauseEnCoursMs);

                if (tempsEcouleMs >= dureeMs) return true;
            }
        }
    }

    private sealed record DebutQuestion(string? HostConnectionId, BonusQuestionStartedForHostDto PourHost, List<string> Joueurs, BonusQuestionStartedForPlayersDto PourJoueurs);

    /// <summary>Appelé sous session.Lock. Même construction que GameHub.StartRound pour les rounds
    /// classiques — Mode Qcm tiré au hasard côté BonusRoundService.CreerBonusRound, feintes appliquées
    /// ici (retour utilisateur 2026-08-27 : QCM/Première lettre aussi en question bonus).</summary>
    private DebutQuestion? PreparerQuestion(GameSession session, BonusRound bonusRound, SeriesConfig config)
    {
        var track = tracksRepository.GetById(bonusRound.TrackId);
        if (track is null) return null;

        var (qcmOptions, roundOptions) = GameHub.ConstruireQcmOptions(bonusRound.QcmOptionTrackIds, track, bonusRound.Cible, session.Config, tracksRepository);
        bonusRound.Options = roundOptions;
        var anneeOptions = bonusRound.AnneeOptions?.Select(a => a.ToString()).ToList();

        var pourHost = GameHub.BonusQuestionPourHost(session, bonusRound, track, config, qcmOptions, anneeOptions);
        var joueurs = session.Players.Where(p => p.ConnectionId is not null).Select(p => p.ConnectionId!).ToList();
        var pourJoueurs = new BonusQuestionStartedForPlayersDto(bonusRound.Id, config.DureePhaseQuestionMs, bonusRound.Cible, session.SerieCourante().Index, bonusRound.Mode, qcmOptions, bonusRound.EstCourse, TempsEcouleMs: 0, AnneeOptions: anneeOptions);
        return new DebutQuestion(session.HostConnectionId, pourHost, joueurs, pourJoueurs);
    }

    private async Task DiffuserDebutPhaseQuestionAsync(GameSession session, DebutQuestion debut)
    {
        if (debut.HostConnectionId is not null)
            await hubContext.Clients.Client(debut.HostConnectionId).SendAsync("BonusQuestionStarted", debut.PourHost);

        await hubContext.Clients.Clients(debut.Joueurs).SendAsync("BonusQuestionStarted", debut.PourJoueurs);
    }
}
