namespace Blindify.Domain.Enums;

/// <summary>Raison d'un signalement en direct (V2, section 12.4) — voir FlagEntryDto. Choisie par le
/// host ou un admin authentifié depuis l'écran de reveal, jamais par un joueur simple.</summary>
public enum RaisonSignalement
{
    /// <summary>Morceau qui n'a rien à faire dans le catalogue (trop obscur, inapproprié, doublon).</summary>
    PasSaPlace,

    /// <summary>Live, remix, reprise, mauvais match YouTube.</summary>
    MauvaiseVersion,

    /// <summary>Coupure, silence, volume, mauvaise qualité.</summary>
    AudioDefectueux,

    /// <summary>Titre, artiste, album ou année erronés.</summary>
    MetadonneesFausses,

    /// <summary>Ne correspond pas au thème (tags) de la série.</summary>
    HorsTheme,

    /// <summary>Track.RefrainStartMs à corriger.</summary>
    RefrainMalPlace,

    /// <summary>Commentaire obligatoire (voir GameHub.SignalerMorceau) — catégorie non prévue ci-dessus.</summary>
    Autre,
}

/// <summary>Classification fixe des raisons "bloquantes" (V2, section 12.4) — un morceau avec un
/// signalement bloquant non résolu est exclu de RoundService.SelectionnerMorceaux dès le prochain
/// ConfigurerPartie/RejouerPartie (voir GameConfig.ExclureMorceauxSignales). Une raison non bloquante
/// (thème, refrain, autre) n'empêche jamais le morceau d'être retiré — elle sert seulement à alimenter
/// le pipeline de curation (data/scripts/export_flags_csv.py).</summary>
public static class RaisonSignalementRules
{
    public static bool EstBloquante(RaisonSignalement raison) => raison switch
    {
        RaisonSignalement.PasSaPlace => true,
        RaisonSignalement.MauvaiseVersion => true,
        RaisonSignalement.AudioDefectueux => true,
        RaisonSignalement.MetadonneesFausses => true,
        _ => false,
    };
}
