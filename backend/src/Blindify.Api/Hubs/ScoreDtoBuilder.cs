using Blindify.Api.Contracts;
using Blindify.Application.Rounds;
using Blindify.Domain.Entities;

namespace Blindify.Api.Hubs;

internal static class ScoreDtoBuilder
{
    public static ScoreUpdateDto Construire(GameSession session)
    {
        var joueurs = session.Players.Select(p => new PlayerScoreDto(p.PlayerId, p.Nom, p.Score, p.TeamId)).ToList();

        var equipes = session.ModeEquipe
            ? session.ScoresParEquipe().Select(e => new TeamScoreDto(e.Team.Id, e.Team.Nom, e.Score)).ToList()
            : null;

        return new ScoreUpdateDto(joueurs, equipes);
    }
}
