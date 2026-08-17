using Blindify.Domain.Enums;

namespace Blindify.Api.Contracts;

public record QcmOptionDto(string TrackId, string Title, string Artist);

/// <summary>Envoyé uniquement au host — inclut le chemin audio (jamais transmis aux joueurs).
/// RefrainStartMs (ms) : point de départ à jouer si renseigné dans tracks.json, sinon lecture
/// depuis le début du fichier.</summary>
public record RoundStartedForHostDto(RoundMode Mode, RoundCible Cible, string TrackId, string FilePath, int? RefrainStartMs, int DureeFenetreReponseMs);

/// <summary>Envoyé aux joueurs — pas d'audio, options QCM si applicable. Cible indique ce qui est
/// demandé (titre ou auteur) — à afficher pour lever l'ambiguïté sur les morceaux à plusieurs auteurs.</summary>
public record RoundStartedForPlayersDto(RoundMode Mode, RoundCible Cible, int DureeFenetreReponseMs, List<QcmOptionDto>? QcmOptions);

public record SubmitAnswerRequestDto(string Reponse);

public record RoundAnswerResultDto(bool EstCorrecte, int Points, int NouveauScore);

public record RoundResultEntryDto(string PlayerId, string? Reponse, bool? EstCorrecte, int Points);

public record RoundEndedDto(string TrackId, string Title, string Artist, List<RoundResultEntryDto> Resultats);
