namespace Blindify.Domain.Enums;

/// <summary>Ce qui est demandé au joueur pour un round donné — tiré aléatoirement au démarrage
/// (voir RoundService.DemarrerRound), affiché aux joueurs pour lever l'ambiguïté quand un morceau
/// a plusieurs auteurs.</summary>
public enum RoundCible
{
    Titre,
    Auteur,

    /// <summary>Forcée pour les morceaux taggés "disney" (voir RoundService.DemarrerRound) : le
    /// titre réel de la chanson ou l'artiste crédité (souvent la voix/l'acteur) sont imprévisibles
    /// à deviner — le film dont est tiré le morceau (Track.Album nettoyé, voir FilmNameResolver)
    /// est la question naturelle pour ce type de contenu.</summary>
    Film
}
