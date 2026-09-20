namespace Blindify.Api.Contracts;

/// <summary>V2, section 12.7 — un seul type pour les 5 combinaisons mode/cible possibles (voir
/// JokerIndice côté domaine), chaque appelant ne renseignant que le(s) champ(s) qui le concernent.
/// OptionsRetirees : TrackId (Qcm normal) ou années en texte (Qcm + cible Annee) — dans les deux cas,
/// exactement les valeurs que le client passerait déjà à SubmitAnswer. CoverUrl : chemin relatif vers
/// GET /api/joker/cover/{jeton}, uniquement pour TapeReponse + Titre/Auteur.</summary>
public record JokerIndiceDto(List<string>? OptionsRetirees, List<string>? TuilesRestantes, string? Structure, int? Decennie, string? CoverUrl);

/// <summary>Diffusé à tout le groupe (host, écran public, joueurs) — confirmation visuelle uniquement,
/// jamais l'effet ni la cible du joker (voir GameHub.UtiliserJoker).</summary>
public record JokerUtiliseDto(string PlayerId);
