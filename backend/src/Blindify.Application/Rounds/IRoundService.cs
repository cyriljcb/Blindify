using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;

namespace Blindify.Application.Rounds;

/// <summary>Cycle de vie d'un round classique — voir architecture.md section 6.</summary>
public interface IRoundService
{
    /// <summary>
    /// Tire `nombre` morceaux distincts, en priorité dans le pool genre/tag (`tags`), avec repli sur le
    /// pool global si insuffisant — même esprit que le fallback QCM (section 6). `dejaUtilises` est mis à
    /// jour avec les IDs tirés, pour éviter les répétitions sur le reste de la partie.
    /// </summary>
    List<Track> SelectionnerMorceaux(IReadOnlyList<Track> pool, IReadOnlyList<string> tags, int nombre, HashSet<string> dejaUtilises);

    /// <summary>
    /// Démarre un round déjà pré-créé (morceau + mode assignés à CreateGame) : horodate débutRound et
    /// génère les options QCM si le mode l'exige. Mute `round` en place.
    /// </summary>
    void DemarrerRound(Round round, Track track, IReadOnlyList<Track> catalogueComplet, GameConfig config, DateTimeOffset maintenant);

    /// <summary>
    /// Soumet la réponse d'un joueur. Retourne null si la soumission est invalide (partie en pause,
    /// round pas démarré, joueur a déjà répondu) — le serveur ne fait pas confiance à l'UI client.
    /// Applique les points au score du joueur.
    /// </summary>
    RoundAnswer? SoumettreReponse(GameSession session, Round round, SeriesConfig seriesConfig, Track track, string playerId, string reponse, DateTimeOffset maintenant);

    /// <summary>Applique la pénalité fixe d'absence de réponse à tous les joueurs n'ayant pas répondu.</summary>
    void TerminerParTimeout(GameSession session, Round round, SeriesConfig config);

    /// <summary>
    /// Override manuel (host) d'une réponse texte ambiguë. Recalcule les points à partir du pointsEnJeu
    /// déjà figé au moment de la réponse et applique le delta au score du joueur. Retourne null si le
    /// joueur n'a pas de réponse enregistrée pour ce round.
    /// </summary>
    RoundAnswer? ValiderManuellement(GameSession session, Round round, SeriesConfig config, string playerId, bool estCorrecte);
}
