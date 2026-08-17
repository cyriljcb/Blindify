namespace Blindify.Domain.Enums;

/// <summary>Ce qui est demandé au joueur pour un round donné — tiré aléatoirement au démarrage
/// (voir RoundService.DemarrerRound), affiché aux joueurs pour lever l'ambiguïté quand un morceau
/// a plusieurs auteurs.</summary>
public enum RoundCible
{
    Titre,
    Auteur
}
