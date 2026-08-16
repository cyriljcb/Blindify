using Blindify.Domain.Entities;

namespace Blindify.Application.Rounds;

/// <summary>Navigation dans la progression série/round courante d'une GameSession.</summary>
public static class GameSessionNavigation
{
    public static Series SerieCourante(this GameSession session) => session.SeriesList[session.SerieCouranteIndex];

    public static Round? RoundCourant(this GameSession session)
    {
        var serie = session.SerieCourante();
        return session.RoundCourantIndex >= 0 && session.RoundCourantIndex < serie.Rounds.Count
            ? serie.Rounds[session.RoundCourantIndex]
            : null;
    }

    /// <summary>Score d'équipe = somme des points gagnés par ses membres — voir architecture.md section 8.</summary>
    public static IEnumerable<(Team Team, int Score)> ScoresParEquipe(this GameSession session) =>
        session.Teams.Select(team => (team, session.Players.Where(p => p.TeamId == team.Id).Sum(p => p.Score)));
}
