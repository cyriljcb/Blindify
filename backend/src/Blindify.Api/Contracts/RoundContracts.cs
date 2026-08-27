using Blindify.Domain.Enums;

namespace Blindify.Api.Contracts;

/// <summary>Film : nom nettoyé du film d'origine (voir FilmNameResolver), affiché à la place de
/// Title/Artist quand la cible du round est RoundCible.Film (morceaux "disney").</summary>
public record QcmOptionDto(string TrackId, string Title, string Artist, string Film);

/// <summary>Envoyé uniquement au host — inclut le chemin audio (jamais transmis aux joueurs).
/// RefrainStartMs (ms) : point de départ à jouer si renseigné dans tracks.json, sinon lecture
/// depuis le début du fichier. QcmOptions : mêmes options que côté joueurs (retour utilisateur :
/// affichées aussi sur l'écran public) — sans risque, ce sont les choix proposés aux joueurs de
/// toute façon, pas la réponse correcte.</summary>
public record RoundStartedForHostDto(RoundMode Mode, RoundCible Cible, string TrackId, string FilePath, int? RefrainStartMs, int DureeFenetreReponseMs, List<QcmOptionDto>? QcmOptions);

/// <summary>Envoyé aux joueurs — pas d'audio, options QCM si applicable. Cible indique ce qui est
/// demandé (titre ou auteur) — à afficher pour lever l'ambiguïté sur les morceaux à plusieurs auteurs.
/// SerieIndex (0-based) : permet d'afficher "Série A/B/C..." côté joueur, cohérent avec le
/// libellage par lettre du panneau host et de l'écran public (retour utilisateur).</summary>
public record RoundStartedForPlayersDto(RoundMode Mode, RoundCible Cible, int DureeFenetreReponseMs, int SerieIndex, List<QcmOptionDto>? QcmOptions);

public record SubmitAnswerRequestDto(string Reponse);

public record RoundAnswerResultDto(bool EstCorrecte, int Points, int NouveauScore);

public record RoundResultEntryDto(string PlayerId, string? Reponse, bool? EstCorrecte, int Points);

/// <summary>Cible/Film : répétés ici (déjà connus du client depuis RoundStarted) pour que l'écran
/// de révélation puisse afficher le nom du film comme réponse quand Cible == Film, sans avoir à
/// faire correspondre ce round à l'événement RoundStarted précédent côté client.</summary>
public record RoundEndedDto(string TrackId, string Title, string Artist, string? CoverPath, RoundCible Cible, string Film, List<RoundResultEntryDto> Resultats);
