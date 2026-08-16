using Blindify.Domain.Enums;

namespace Blindify.Api.Contracts;

public record QcmOptionDto(string TrackId, string Title, string Artist);

/// <summary>Envoyé uniquement au host — inclut le chemin audio (jamais transmis aux joueurs).</summary>
public record RoundStartedForHostDto(RoundMode Mode, string TrackId, string FilePath, int DureeFenetreReponseMs);

/// <summary>Envoyé aux joueurs — pas d'audio, options QCM si applicable.</summary>
public record RoundStartedForPlayersDto(RoundMode Mode, int DureeFenetreReponseMs, List<QcmOptionDto>? QcmOptions);

public record SubmitAnswerRequestDto(string Reponse);

public record RoundAnswerResultDto(bool EstCorrecte, int Points, int NouveauScore);

public record RoundResultEntryDto(string PlayerId, string? Reponse, bool? EstCorrecte, int Points);

public record RoundEndedDto(string TrackId, string Title, string Artist, List<RoundResultEntryDto> Resultats);
