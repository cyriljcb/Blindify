using Blindify.Domain.Enums;

namespace Blindify.Infrastructure.Flags;

public interface IFlagsRepository
{
    /// <summary>Ajoute un signalement, sauf si le même morceau a déjà été signalé pour la même raison
    /// dans la même partie (retourne alors l'entrée existante, DejaSignale=true) — voir
    /// GameHub.SignalerMorceau.</summary>
    (string FlagId, bool DejaSignale) Ajouter(
        string trackId, RaisonSignalement raison, string? commentaire, string par, string gameCode,
        IReadOnlyList<string> serieTags, RoundCible cible, RoundMode mode);

    /// <summary>Ids de morceaux à exclure de RoundService.SelectionnerMorceaux (V2, section 12.4) :
    /// signalement bloquant (RaisonSignalementRules.EstBloquante) ET encore non résolu dans
    /// flags_resolutions.json.</summary>
    HashSet<string> ObtenirTrackIdsBloquants();
}
