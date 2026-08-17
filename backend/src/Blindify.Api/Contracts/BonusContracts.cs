namespace Blindify.Api.Contracts;

public record BonusStakeOptionsDto(int[] Paliers, int DureePhaseMiseMs);

public record SelectStakeRequestDto(int PalierIndex);

/// <summary>Envoyé au host uniquement — inclut l'audio (ralenti) du morceau révélé.
/// RefrainStartMs : comme pour un round classique, appliqué au reveal (BonusResult), pas pendant
/// la phase question qui reste jouée depuis le début (c'est la devinette elle-même).</summary>
public record BonusQuestionStartedForHostDto(string TrackId, string FilePath, int? RefrainStartMs, int DureePhaseQuestionMs, bool RalentissementActive, double FacteurRalentissement);

/// <summary>Envoyé aux joueurs — pas d'audio, juste le signal de démarrage de la phase question.</summary>
public record BonusQuestionStartedForPlayersDto(int DureePhaseQuestionMs);

public record SubmitBonusAnswerRequestDto(string Reponse);

public record BonusAnswerResultDto(bool EstCorrecte, int Points, int NouveauScore);

public record BonusResultEntryDto(string PlayerId, int Mise, string? Reponse, bool EstCorrecte, int Points);

public record BonusResultDto(string TrackId, string Title, string Artist, string? CoverPath, List<BonusResultEntryDto> Resultats);
