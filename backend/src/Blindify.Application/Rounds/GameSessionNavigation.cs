using Blindify.Domain.Entities;

namespace Blindify.Application.Rounds;

/// <summary>Navigation dans la progression série/round courante d'une GameSession.</summary>
public static class GameSessionNavigation
{
    public static Series SerieCourante(this GameSession session) => session.SeriesList[session.SerieCouranteIndex];

    public static Round? RoundCourant(this GameSession session)
    {
        // Lobby sans configuration encore soumise (voir GameHub.ConfigurerPartie) — SeriesList est
        // vide jusqu'au premier appel, un état désormais atteignable (CreateGame ne configure plus
        // rien) qui ne l'était pas avant le retour utilisateur du 2026-08-24.
        if (session.SeriesList.Count == 0) return null;

        var serie = session.SerieCourante();
        return session.RoundCourantIndex >= 0 && session.RoundCourantIndex < serie.Rounds.Count
            ? serie.Rounds[session.RoundCourantIndex]
            : null;
    }

    /// <summary>Score d'équipe = somme des points gagnés par ses membres — voir architecture.md section 8.</summary>
    public static IEnumerable<(Team Team, int Score)> ScoresParEquipe(this GameSession session) =>
        session.Teams.Select(team => (team, session.Players.Where(p => p.TeamId == team.Id).Sum(p => p.Score)));
}
