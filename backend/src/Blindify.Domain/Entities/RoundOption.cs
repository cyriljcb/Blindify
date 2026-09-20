namespace Blindify.Domain.Entities;

/// <summary>Une option QCM réellement présentée aux joueurs (V2, socle statistiques) — persistée sur
/// Round/BonusRound au moment de la construction du QCM (StartRound / BonusTimerCoordinator), pas
/// recalculée à chaque fois : sans ça, un joueur qui se reconnecte pouvait voir un tirage de feinte
/// différent de celui vu par les autres (AppliquerFeinteEventuelle/AppliquerFeinteTexteEventuelle
/// sont probabilistes), et il était impossible de savoir après coup quelles options avaient
/// effectivement été affichées pour calculer les confusions (data/scripts/suggest_traps.py).</summary>
public class RoundOption
{
    public required string TrackId { get; set; }

    /// <summary>Texte affiché pour LA cible de ce round (Titre/Auteur/Film) — un seul champ, pas les
    /// trois de QcmOptionDto, puisque la cible du round est déjà fixée et que les deux autres champs
    /// ne sont jamais montrés au joueur. Reflète une éventuelle feinte (voir EstFeinte).</summary>
    public required string TexteAffiche { get; set; }

    /// <summary>Texte substitué par une feinte (champ croisé ou trapTextArtist) — voir
    /// GameHub.AppliquerFeinteEventuelle/AppliquerFeinteTexteEventuelle. Exclu du calcul des
    /// confusions (mesurerait une confusion de texte, pas de morceau).</summary>
    public bool EstFeinte { get; set; }

    /// <summary>Ce distracteur vient de Track.TrapWith du morceau correct (piège réel, deux morceaux
    /// réellement confondus) — contrairement à EstFeinte, reste compté dans les confusions.</summary>
    public bool EstPiege { get; set; }
}
