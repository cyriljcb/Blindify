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
    /// `playCount` (optionnel) fournit le nombre de fois où un morceau a déjà été joué (toutes parties
    /// confondues) : quand fourni, le tirage est pondéré en faveur des morceaux les moins joués plutôt
    /// que purement uniforme — retour utilisateur : le compteur existait déjà (data/stats.json) mais
    /// n'influençait jamais la sélection, donc un même thème ressortait souvent avec les mêmes morceaux
    /// d'une partie à l'autre. Poids doux (jamais nul), voir RoundService.TirerPondere.
    /// </summary>
    List<Track> SelectionnerMorceaux(IReadOnlyList<Track> pool, IReadOnlyList<string> tags, int nombre, HashSet<string> dejaUtilises, Func<string, int>? playCount = null);

    /// <summary>
    /// Démarre un round déjà pré-créé (morceau + mode assignés à CreateGame) : horodate débutRound et
    /// génère les options QCM si le mode l'exige. Mute `round` en place. Les distracteurs QCM sont
    /// tirés du pool filtré par `tags` (thème de la partie/série) — jamais du catalogue complet
    /// hors-thème, sauf pool insuffisant (repli, même esprit que SelectionnerMorceaux). Exception :
    /// cible Film (morceaux "disney"), où seuls d'autres morceaux "disney" ont un nom de film
    /// cohérent à proposer — pas de repli catalogue complet dans ce cas précis.
    /// </summary>
    void DemarrerRound(Round round, Track track, IReadOnlyList<Track> catalogueComplet, IReadOnlyList<string> tags, GameConfig config, DateTimeOffset maintenant);

    /// <summary>
    /// Soumet la réponse d'un joueur. Retourne null si la soumission est invalide (partie en pause,
    /// round pas démarré, joueur a déjà répondu) — le serveur ne fait pas confiance à l'UI client.
    /// Applique les points au score du joueur.
    /// </summary>
    /// <param name="resolveTrack">
    /// Résout un TrackId en Track, utilisé uniquement en mode Qcm pour retomber sur une comparaison
    /// par libellé affiché (voir RoundService.EstQcmEquivalent) quand le filet de sécurité de
    /// QcmGenerator a dû proposer un distracteur du même auteur/titre que la bonne réponse (catalogue
    /// trop restreint pour ce thème) — sans ça, cliquer l'option qui affiche pourtant le bon texte
    /// pouvait être compté faux (retour utilisateur : "Myles Smith" en double, mauvais TrackId cliqué).
    /// Optionnel (null = comparaison stricte par TrackId uniquement) pour ne pas casser les appels
    /// existants qui n'ont pas besoin de cette résolution.
    /// </param>
    RoundAnswer? SoumettreReponse(GameSession session, Round round, SeriesConfig seriesConfig, Track track, string playerId, string reponse, DateTimeOffset maintenant, Func<string, Track?>? resolveTrack = null);

    /// <summary>Applique la pénalité fixe d'absence de réponse à tous les joueurs n'ayant pas répondu.</summary>
    void TerminerParTimeout(GameSession session, Round round, SeriesConfig config);

    /// <summary>
    /// Override manuel (host) d'une réponse texte ambiguë. Recalcule les points à partir du pointsEnJeu
    /// déjà figé au moment de la réponse et applique le delta au score du joueur. Retourne null si le
    /// joueur n'a pas de réponse enregistrée pour ce round.
    /// </summary>
    RoundAnswer? ValiderManuellement(GameSession session, Round round, SeriesConfig config, string playerId, bool estCorrecte);
}
