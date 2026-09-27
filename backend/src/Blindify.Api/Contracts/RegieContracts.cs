namespace Blindify.Api.Contracts;

/// <summary>Refonte UI (lot 3) — configuration de partie enregistrée (data/presets.json), voir
/// GameHub.ListerPresets/EnregistrerPreset/SupprimerPreset. MusiqueContinue et DelaiEnchainementMs
/// ne sont utilisés que par la page host (jamais transmis à ConfigurerPartie).</summary>
public record PresetDto(
    string Nom,
    int NombreSeries,
    int NombreRoundsClassiques,
    int DureeFenetreReponseMs,
    List<string> ThemesVivier,
    bool MusiqueContinue,
    int DelaiEnchainementMs,
    bool AfficherEcart);

/// <summary>Refonte UI (lot 3) — commande envoyée par la télécommande d'un admin (téléphone) et
/// relayée à la page host, qui l'exécute comme le raccourci clavier correspondant (voir
/// GameHub.EnvoyerCommandeHost). "suivant" = Espace (action suivante de l'écran courant),
/// "reecouter" = R.</summary>
public record CommandeHostDto(string Commande);

/// <summary>Refonte UI (lot 3) — ce que ferait « Action suivante » sur la page host à cet instant
/// (libellé du bouton, disponible ou non), publié par le host à chaque changement et relayé aux
/// admins pour l'afficher sur la télécommande. Jamais d'information sur le morceau.</summary>
public record EtatRegieDto(string LibelleAction, bool ActionDisponible);
