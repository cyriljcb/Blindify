using Blindify.Domain.Enums;

namespace Blindify.Api.Contracts;

/// <summary>Phase de jeu côté host, telle que reconstruite par GameHub.RejoinAsHost.
/// AvantSerie : partie démarrée, aucun round de la série courante encore lancé (écran d'annonce).
/// RoundPret : round précédent révélé et NextRound déjà appelé, le round courant n'est pas encore
/// lancé (le host doit seulement appeler StartRound).</summary>
public enum PhaseHost
{
    Lobby,
    AvantSerie,
    Round,
    RoundTermine,
    RoundPret,
    BonusMise,
    BonusQuestion,
    BonusTermine,
    Termine,
}

/// <summary>Renvoyé par RejoinAsHost (audit 2026-11-10) — état complet pour que la page host
/// reprenne la partie là où elle en était après un rechargement ou une reconnexion, au lieu de
/// retomber sur le lobby. Les sous-objets réutilisent les DTO des événements correspondants : la
/// page host les rejoue dans ses handlers habituels. Un seul des groupes Round/RoundEnded/Bonus*
/// est renseigné selon Phase (RoundPret renseigne RoundEnded, celui du round précédent).
/// PositionAudioMs : position de lecture à reprendre (round ou question bonus en cours).
/// TempsEcouleMs : temps déjà écoulé de la phase chronométrée en cours (hors pause).
/// Repondants : joueurs ayant déjà répondu à la question en cours, dans l'ordre d'arrivée.
/// MisesEnregistrees : joueurs ayant déjà misé (phase de mise).</summary>
public record HostStateSnapshotDto(
    bool EnPause,
    PhaseHost Phase,
    bool PartieConfiguree,
    int NombreSeries,
    int NombreRoundsParSerie,
    int SerieCouranteIndex,
    int RoundsDemarresDansSerie,
    List<List<string>> TagsParSerie,
    List<PlayerSummaryDto> Joueurs,
    List<TeamDto> Equipes,
    ScoreUpdateDto Scores,
    VotesThemesDto? VotesThemes,
    RoundStartedForHostDto? Round,
    RoundEndedDto? RoundEnded,
    BonusStakeOptionsDto? BonusMise,
    List<string>? MisesEnregistrees,
    BonusQuestionStartedForHostDto? BonusQuestion,
    BonusResultDto? BonusResult,
    GameEndedDto? GameEnded,
    long? PositionAudioMs,
    long? TempsEcouleMs,
    List<PlayerAnsweredDto>? Repondants);
