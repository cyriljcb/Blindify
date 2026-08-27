namespace Blindify.Application.Rounds;

/// <summary>Un morceau peut avoir plusieurs auteurs listés dans un seul champ ("A, B, C") —
/// n'importe lequel d'entre eux est une réponse valable (featurings, voir retour utilisateur :
/// exiger la liste complète est ingérable dès qu'il y a plus d'un featuring). Partagé entre
/// RoundService (round classique) et BonusRoundService (question bonus).</summary>
public static class AuteurVariantes
{
    public static IEnumerable<string> Acceptables(string artist) =>
        artist.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
