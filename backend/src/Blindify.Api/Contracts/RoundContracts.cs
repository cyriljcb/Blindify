using Blindify.Domain.Enums;

namespace Blindify.Api.Contracts;

/// <summary>Film : nom nettoyé du film d'origine (voir FilmNameResolver), affiché à la place de
/// Title/Artist quand la cible du round est RoundCible.Film (morceaux "disney").</summary>
public record QcmOptionDto(string TrackId, string Title, string Artist, string Film);

/// <summary>Envoyé uniquement au host — inclut le chemin audio (jamais transmis aux joueurs).
/// RoundId (V2) : identité du round, voir Round.Id — le host n'en a pas l'usage aujourd'hui (il ne
/// soumet jamais de réponse) mais le porte pour rester symétrique avec la variante joueurs.
/// RefrainStartMs (ms) : point de départ à jouer si renseigné dans tracks.json, sinon lecture
/// depuis le début du fichier. QcmOptions : mêmes options que côté joueurs (retour utilisateur :
/// affichées aussi sur l'écran public) — sans risque, ce sont les choix proposés aux joueurs de
/// toute façon, pas la réponse correcte. AnneeOptions (V2, section 12.5) : uniquement peuplé pour
/// Cible.Annee + Mode.Qcm, années en texte triées — jamais combiné avec QcmOptions (l'un ou l'autre
/// selon la cible).</summary>
public record RoundStartedForHostDto(RoundMode Mode, RoundCible Cible, Guid RoundId, string TrackId, string FilePath, int? RefrainStartMs, int DureeFenetreReponseMs, List<QcmOptionDto>? QcmOptions, List<string>? AnneeOptions = null);

/// <summary>Envoyé aux joueurs — pas d'audio, options QCM si applicable. Cible indique ce qui est
/// demandé (titre ou auteur) — à afficher pour lever l'ambiguïté sur les morceaux à plusieurs auteurs.
/// RoundId (V2) : à renvoyer tel quel dans SubmitAnswerRequestDto — le serveur ignore silencieusement
/// toute soumission dont le RoundId ne correspond plus au round courant (reconnexion tardive, requête
/// en vol au moment où le round suivant démarre), voir GameHub.SubmitAnswer.
/// SerieIndex (0-based) : permet d'afficher "Série A/B/C..." côté joueur, cohérent avec le
/// libellage par lettre du panneau host et de l'écran public (retour utilisateur).
/// TempsEcouleMs : 0 au démarrage normal du round ; temps déjà écoulé quand ce DTO est réutilisé
/// pour resynchroniser un joueur qui (re)rejoint en pleine partie (voir EtatCourantJoueurDto) —
/// sans ça, la barre de temps du client redémarrait à la durée totale au lieu du temps restant.
/// AnneeOptions : voir RoundStartedForHostDto.</summary>
public record RoundStartedForPlayersDto(RoundMode Mode, RoundCible Cible, Guid RoundId, int DureeFenetreReponseMs, int SerieIndex, List<QcmOptionDto>? QcmOptions, int TempsEcouleMs, List<string>? AnneeOptions = null);

/// <summary>Diffusé à tout le groupe (host, écran public, autres joueurs) dès qu'une réponse est
/// enregistrée — voir GameHub.SubmitAnswer/SubmitBonusAnswer. Volontairement minimal : ni la
/// réponse ni son exactitude n'y figurent (l'écran public ne montre jamais d'information à deviner,
/// voir CLAUDE.md) — seulement de quoi réagir visuellement et afficher un classement de rapidité.
/// TempsEcouleMs mesuré exactement comme pour le calcul des points (voir IScoringService),
/// hors durée de pause.</summary>
public record PlayerAnsweredDto(string PlayerId, int TempsEcouleMs);

/// <summary>RoundId (V2) : repris de RoundStartedForPlayersDto, voir ce DTO — permet au serveur de
/// rejeter silencieusement une réponse arrivée pour un round qui n'est déjà plus le round courant.</summary>
public record SubmitAnswerRequestDto(Guid RoundId, string Reponse);

public record RoundAnswerResultDto(bool EstCorrecte, int Points, int NouveauScore);

/// <summary>EcartAnnee (V2, section 12.5) : écart en années uniquement pour la cible Année en mode
/// saisie, null sinon (ou si la réponse n'était pas un nombre valide).</summary>
public record RoundResultEntryDto(string PlayerId, string? Reponse, bool? EstCorrecte, int Points, int? EcartAnnee = null);

/// <summary>Cible/Film : répétés ici (déjà connus du client depuis RoundStarted) pour que l'écran
/// de révélation puisse afficher le nom du film comme réponse quand Cible == Film, sans avoir à
/// faire correspondre ce round à l'événement RoundStarted précédent côté client.</summary>
/// <summary>Annee (V2, section 12.5) : Track.Year tel quel, pour que le reveal puisse afficher
/// l'année réelle quand Cible == Annee (le host/l'écran public n'ont sinon aucun moyen de la
/// connaître — jamais transmise avant le reveal). Null si le morceau n'a pas d'année renseignée.</summary>
public record RoundEndedDto(string TrackId, string Title, string Artist, string? CoverPath, RoundCible Cible, string Film, List<RoundResultEntryDto> Resultats, int? Annee = null);
