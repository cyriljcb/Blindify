using Blindify.Domain.Enums;

namespace Blindify.Api.Contracts;

/// <summary>Voir GameHub.SignalerMorceau — host ou admin authentifié uniquement, jamais les joueurs.</summary>
public record SignalementRequestDto(string TrackId, RaisonSignalement Raison, string? Commentaire);

/// <summary>DejaSignale : même morceau + même raison déjà signalés dans cette partie — pas de doublon
/// créé, FlagId pointe vers l'entrée existante.</summary>
public record SignalementResultDto(string FlagId, bool DejaSignale);

/// <summary>Diffusé uniquement au host et aux admins authentifiés (jamais Clients.Group, jamais aux
/// joueurs) — simple confirmation visuelle qu'un signalement a bien été enregistré.</summary>
public record MorceauSignaleDto(string TrackId, string Titre, string Artiste, RaisonSignalement Raison);
