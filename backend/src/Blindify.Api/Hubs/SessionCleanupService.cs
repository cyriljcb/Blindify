using Blindify.Application.Sessions;

namespace Blindify.Api.Hubs;

/// <summary>Purge les salons abandonnés (audit 2026-11-10) : l'état est 100 % en mémoire et les
/// sessions n'étaient retirées que par FermerSalon — un onglet host fermé laissait un salon zombie
/// que l'app rejoignait automatiquement au lancement suivant (code persisté). Un salon est retiré
/// après DelaiInactivite sans aucune action host/joueur (GameSession.DerniereActiviteA), quel que
/// soit son état : une soirée de jeu touche la session bien plus souvent que ça.</summary>
public class SessionCleanupService(
    IGameSessionStore sessionStore,
    RoundTimerCoordinator roundTimers,
    BonusTimerCoordinator bonusTimers,
    ILogger<SessionCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan DelaiInactivite = TimeSpan.FromHours(6);
    private static readonly TimeSpan IntervalleVerification = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(IntervalleVerification, stoppingToken);
                Purger();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Purge des salons inactifs impossible");
            }
        }
    }

    internal void Purger()
    {
        var limite = DateTimeOffset.UtcNow - DelaiInactivite;
        foreach (var session in sessionStore.Lister())
        {
            DateTimeOffset derniereActivite;
            lock (session.Lock) { derniereActivite = session.DerniereActiviteA; }
            if (derniereActivite >= limite) continue;

            roundTimers.Annuler(session.Id);
            bonusTimers.Annuler(session.Id);
            sessionStore.Remove(session.Id);
            logger.LogInformation("Salon {Code} retiré après {Heures} h d'inactivité", session.Id, DelaiInactivite.TotalHours);
        }
    }
}
