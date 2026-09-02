using Blindify.Domain.Enums;

namespace Blindify.Api.Contracts;

/// <summary>SerieIndex (0-based) : voir RoundStartedForPlayersDto — même besoin de libellage par
/// lettre côté joueur sur l'écran de mise à l'aveugle.</summary>
public record BonusStakeOptionsDto(int[] Paliers, int DureePhaseMiseMs, int SerieIndex);

public record SelectStakeRequestDto(int PalierIndex);

/// <summary>Envoyé au host uniquement — inclut l'audio (ralenti) du morceau révélé.
/// RefrainStartMs : comme pour un round classique, appliqué au reveal (BonusResult), pas pendant
/// la phase question qui reste jouée depuis le début (c'est la devinette elle-même). Mode/QcmOptions :
/// mêmes rôles que RoundStartedForHostDto — Mode tiré au hasard comme un round classique. EstCourse :
/// voir BonusRound.EstCourse, révélé seulement ici (jamais pendant la mise à l'aveugle).</summary>
public record BonusQuestionStartedForHostDto(string TrackId, string FilePath, int? RefrainStartMs, int DureePhaseQuestionMs, bool RalentissementActive, double FacteurRalentissement, RoundMode Mode, List<QcmOptionDto>? QcmOptions, bool EstCourse);

/// <summary>Envoyé aux joueurs — pas d'audio. Cible (Titre/Film) indique ce qui est demandé, comme
/// pour RoundStartedForPlayersDto — toujours Titre sauf morceau "disney" (Film). SerieIndex :
/// voir RoundStartedForPlayersDto. Mode/QcmOptions : voir RoundStartedForPlayersDto. EstCourse :
/// voir BonusQuestionStartedForHostDto.</summary>
public record BonusQuestionStartedForPlayersDto(int DureePhaseQuestionMs, RoundCible Cible, int SerieIndex, RoundMode Mode, List<QcmOptionDto>? QcmOptions, bool EstCourse);

public record SubmitBonusAnswerRequestDto(string Reponse);

public record BonusAnswerResultDto(bool EstCorrecte, int Points, int NouveauScore);

public record BonusResultEntryDto(string PlayerId, int Mise, string? Reponse, bool EstCorrecte, int Points);

/// <summary>Cible/Film : mêmes rôles que RoundEndedDto — permet au reveal d'afficher le film plutôt
/// que le titre réel quand Cible == Film. EstCourse : voir BonusRound.EstCourse — les joueurs absents
/// de Resultats lors d'une course n'ont ni gagné ni perdu (mise rendue intacte), à distinguer d'une
/// absence de réponse hors course (qui, elle, perd la mise et apparaît dans Resultats).</summary>
public record BonusResultDto(string TrackId, string Title, string Artist, string? CoverPath, RoundCible Cible, string Film, List<BonusResultEntryDto> Resultats, bool EstCourse);
