using Blindify.Domain.Enums;

namespace Blindify.Api.Contracts;

/// <summary>Renvoyé par RejoinAsHost — permet au host de reprendre l'audio sans redémarrer le morceau.</summary>
public record HostStateSnapshotDto(
    bool EnPause,
    RoundMode? ModeCourant,
    RoundCible? CibleCourante,
    string? TrackId,
    string? FilePath,
    int? RefrainStartMs,
    long? PositionAudioMs,
    int? DureeFenetreReponseMs);
