using Blindify.Domain.Enums;

namespace Blindify.Api.Contracts;

/// <summary>RoundId (V2) : identité du BonusRound (voir BonusRound.Id), stable entre la phase mise et la
/// phase question — à renvoyer tel quel dans SelectStakeRequestDto/SubmitBonusAnswerRequestDto.
/// SerieIndex (0-based) : voir RoundStartedForPlayersDto — même besoin de libellage par
/// lettre côté joueur sur l'écran de mise à l'aveugle. TempsEcouleMs : voir
/// RoundStartedForPlayersDto.</summary>
public record BonusStakeOptionsDto(int[] Paliers, Guid RoundId, int DureePhaseMiseMs, int SerieIndex, int TempsEcouleMs);

/// <summary>RoundId : voir BonusStakeOptionsDto.</summary>
public record SelectStakeRequestDto(Guid RoundId, int PalierIndex);

/// <summary>Envoyé au host uniquement — inclut l'audio (ralenti) du morceau révélé. RoundId : voir
/// BonusStakeOptionsDto. RefrainStartMs : comme pour un round classique, appliqué au reveal
/// (BonusResult), pas pendant la phase question qui reste jouée depuis le début (c'est la devinette
/// elle-même). Mode/QcmOptions : mêmes rôles que RoundStartedForHostDto — Mode tiré au hasard comme un
/// round classique. EstCourse : voir BonusRound.EstCourse, révélé seulement ici (jamais pendant la mise
/// à l'aveugle). AnneeOptions : voir RoundStartedForHostDto.</summary>
public record BonusQuestionStartedForHostDto(string TrackId, string FilePath, int? RefrainStartMs, Guid RoundId, int DureePhaseQuestionMs, bool RalentissementActive, double FacteurRalentissement, RoundMode Mode, List<QcmOptionDto>? QcmOptions, bool EstCourse, List<string>? AnneeOptions = null);

/// <summary>Envoyé aux joueurs — pas d'audio. RoundId : voir BonusStakeOptionsDto. Cible (Titre/Film)
/// indique ce qui est demandé, comme pour RoundStartedForPlayersDto — toujours Titre sauf morceau
/// "disney" (Film). SerieIndex : voir RoundStartedForPlayersDto. Mode/QcmOptions : voir
/// RoundStartedForPlayersDto. EstCourse : voir BonusQuestionStartedForHostDto. TempsEcouleMs : voir
/// RoundStartedForPlayersDto. AnneeOptions : voir RoundStartedForHostDto.</summary>
public record BonusQuestionStartedForPlayersDto(Guid RoundId, int DureePhaseQuestionMs, RoundCible Cible, int SerieIndex, RoundMode Mode, List<QcmOptionDto>? QcmOptions, bool EstCourse, int TempsEcouleMs, List<string>? AnneeOptions = null);

/// <summary>RoundId : voir BonusStakeOptionsDto.</summary>
public record SubmitBonusAnswerRequestDto(Guid RoundId, string Reponse);

public record BonusAnswerResultDto(bool EstCorrecte, int Points, int NouveauScore);

/// <summary>EcartAnnee : voir RoundResultEntryDto.</summary>
public record BonusResultEntryDto(string PlayerId, int Mise, string? Reponse, bool EstCorrecte, int Points, int? EcartAnnee = null);

/// <summary>Cible/Film : mêmes rôles que RoundEndedDto — permet au reveal d'afficher le film plutôt
/// que le titre réel quand Cible == Film. EstCourse : voir BonusRound.EstCourse — les joueurs absents
/// de Resultats lors d'une course n'ont ni gagné ni perdu (mise rendue intacte), à distinguer d'une
/// absence de réponse hors course (qui, elle, perd la mise et apparaît dans Resultats).</summary>
/// <summary>Annee : voir RoundEndedDto.</summary>
public record BonusResultDto(string TrackId, string Title, string Artist, string? CoverPath, RoundCible Cible, string Film, List<BonusResultEntryDto> Resultats, bool EstCourse, int? Annee = null);
