using System.Collections.Concurrent;
using Blindify.Api.Contracts;
using Blindify.Application.Rounds;
using Blindify.Application.Sessions;
using Blindify.Application.Stats;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;
using Blindify.Domain.Statistics;
using Blindify.Infrastructure.Stats;
using Blindify.Infrastructure.Tracks;
using Microsoft.AspNetCore.SignalR;

namespace Blindify.Api.Hubs;

/// <summary>
/// Surveille la fin d'un round classique (durée fixe, indépendante des réponses reçues — architecture.md
/// section 6). Tourne hors du cycle de vie d'un appel de hub, d'où l'usage de IHubContext plutôt que
/// Clients directement.
///
/// Audit 2026-11-10 : toute exception (disque, concurrence) dans la tâche de fond la faisait mourir en
/// silence — RoundEnded n'était jamais diffusé et la partie restait figée. Toutes les collections sont
/// désormais lues sous session.Lock (DTO construits sous verrou), l'écriture des stats a lieu hors
/// verrou dans son propre try/catch, et un filet global logge toute exception restante.
/// </summary>
public class RoundTimerCoordinator(
    IHubContext<GameHub> hubContext,
    IGameSessionStore sessionStore,
    IRoundService roundService,
    ITracksRepository tracksRepository,
    IStatsRepository statsRepository,
    ILogger<RoundTimerCoordinator> logger)
{
    private const int IntervalleVerificationMs = 250;

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _timers = new();

    /// <summary>Vrai si ce round a déjà été clôturé (minuteur ou « Révéler maintenant ») — porté par
    /// le round lui-même (Round.TermineA), à lire sous session.Lock.</summary>
    public bool EstTermine(string code, Round round) => round.TermineA is not null;

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

    /// <summary>Termine le round courant avant la fin du chrono (GameHub.RevelerMaintenant) : mêmes
    /// effets qu'une fin naturelle (absents pénalisés, stats, RoundEnded + ScoreUpdate). Les gardes
    /// métier (tout le monde a répondu, pas de pause) sont vérifiées par l'appelant sous
    /// session.Lock. Retourne false si le round était déjà terminé ou n'existe plus.</summary>
    public async Task<bool> TerminerMaintenantAsync(GameSession session, SeriesConfig config)
    {
        Fin fin;
        lock (session.Lock)
        {
            var round = session.RoundCourant();
            if (round?.DebutRound is null || round.TermineA is not null) return false;
            Annuler(session.Id);
            fin = Terminer(session, round, config);
        }

        await DiffuserFinDeRoundAsync(session, fin);
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

                Fin? fin = null;
                lock (session.Lock)
                {
                    var round = session.RoundCourant();
                    if (round?.DebutRound is null || round.TermineA is not null) return;

                    var pauseEnCoursMs = session.EnPause && session.PauseDemarreeA is not null
                        ? (DateTimeOffset.UtcNow - session.PauseDemarreeA.Value).TotalMilliseconds
                        : 0;
                    var tempsEcouleMs = (DateTimeOffset.UtcNow - round.DebutRound.Value).TotalMilliseconds
                                         - (round.DureeEnPauseMs + pauseEnCoursMs);

                    if (tempsEcouleMs >= config.DureeFenetreReponseMs)
                        fin = Terminer(session, round, config);
                }

                if (fin is not null)
                {
                    await DiffuserFinDeRoundAsync(session, fin);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Annulé volontairement (EndGame/FermerSalon/Révéler maintenant avant l'échéance naturelle).
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chrono du round classique interrompu par une erreur — partie {Code}", code);
        }
        finally
        {
            _timers.TryRemove(code, out _);
        }
    }

    private sealed record Fin(RoundStatsUpdate Stats, RoundEndedDto Reveal, ScoreUpdateDto Scores);

    /// <summary>Appelé sous session.Lock : clôture le round et capture tout ce qui sera diffusé.</summary>
    private Fin Terminer(GameSession session, Round round, SeriesConfig config)
    {
        roundService.TerminerParTimeout(session, round, config);
        session.DerniereActiviteA = DateTimeOffset.UtcNow;
        // V2 (socle statistiques) — Reponses contient déjà les entrées synthétiques EstAbsent. Ne tient
        // pas compte d'un ValidateAnswerManually ultérieur — limitation assumée, voir RoundStatsAggregator.
        return new Fin(
            RoundStatsAggregator.PourRoundClassique(round),
            RevealDtoBuilder.RoundEnded(session, round, tracksRepository),
            ScoreDtoBuilder.Construire(session));
    }

    private async Task DiffuserFinDeRoundAsync(GameSession session, Fin fin)
    {
        await hubContext.Clients.Group(session.Id).SendAsync("RoundEnded", fin.Reveal);
        await hubContext.Clients.Group(session.Id).SendAsync("ScoreUpdate", fin.Scores);

        // Après la diffusion : une écriture disque lente (HDD qui se réveille) ou en échec ne doit
        // jamais retarder ni empêcher le reveal.
        try { statsRepository.EnregistrerResultatsRound(fin.Stats); }
        catch (Exception ex) { logger.LogError(ex, "Écriture des statistiques du round impossible — partie {Code}", session.Id); }
    }
}
