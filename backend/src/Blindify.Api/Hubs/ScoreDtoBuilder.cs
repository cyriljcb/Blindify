using Blindify.Api.Contracts;
using Blindify.Application.Rounds;
using Blindify.Domain.Entities;

namespace Blindify.Api.Hubs;

internal static class ScoreDtoBuilder
{
    /// <summary>Prend session.Lock lui-même (réentrant) : appelé aussi hors verrou depuis le hub, où
    /// une énumération concurrente de Players pouvait lever « Collection was modified ».</summary>
    public static ScoreUpdateDto Construire(GameSession session)
    {
        lock (session.Lock)
        {
            var joueurs = session.Players.Select(p => new PlayerScoreDto(p.PlayerId, p.Nom, p.Score, p.TeamId)).ToList();

            var equipes = session.ModeEquipe
                ? session.ScoresParEquipe().Select(e => new TeamScoreDto(e.Team.Id, e.Team.Nom, e.Score)).ToList()
                : null;

            return new ScoreUpdateDto(joueurs, equipes);
        }
    }
}
