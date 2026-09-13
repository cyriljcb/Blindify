using Blindify.Domain.Enums;

namespace Blindify.Api.Contracts;

public record PlayerSummaryDto(string PlayerId, string Nom, bool EstConnecte, string? TeamId);

/// <summary>Resynchronise un joueur qui (re)rejoint en pleine partie sur la phase actuellement
/// active, pour qu'il puisse répondre au round/à la question bonus en cours plutôt que d'attendre
/// le prochain événement serveur — voir GameHub.ConstruireEtatCourantJoueur. Un seul des trois
/// champs Round/BonusMise/BonusQuestion est renseigné, celui désigné par Phase.
/// DejaRepondu : ce joueur a déjà soumis une réponse/mise pour la phase en cours — la reconnexion
/// ne doit pas lui laisser croire qu'il peut encore répondre (un seul essai par round est de toute
/// façon déjà appliqué côté serveur, mais sans ça l'UI locale rouvrirait la saisie).</summary>
public record EtatCourantJoueurDto(
    PhaseJoueur Phase,
    bool EnPause,
    bool DejaRepondu,
    RoundStartedForPlayersDto? Round,
    BonusStakeOptionsDto? BonusMise,
    BonusQuestionStartedForPlayersDto? BonusQuestion);

/// <summary>Teams : les équipes disponibles (vide si le mode équipe n'est pas actif) — permet au
/// client qui rejoint de proposer un choix sans appel supplémentaire.
/// Joueurs : le roster complet de la partie (soi-même inclus) — sans ça, un joueur qui rejoint
/// après d'autres ne les voyait jamais (PlayerJoined n'est diffusé qu'aux joueurs déjà présents,
/// voir GameHub.JoinGame), y compris lui-même s'il était seul.
/// EtatCourant : null si rien n'est actif (lobby, entre deux rounds, partie terminée) — le client
/// retombe alors sur le lobby en attendant la prochaine diffusion serveur, comportement inchangé.</summary>
public record JoinGameResultDto(bool Success, string? ErrorMessage, int Score, string? TeamId, List<TeamDto> Teams, List<PlayerSummaryDto> Joueurs, EtatCourantJoueurDto? EtatCourant);

public record PlayerJoinedDto(string PlayerId, string Nom);

public record PlayerConnectionChangedDto(string PlayerId, bool EstConnecte);

public record PlayerTeamChangedDto(string PlayerId, string TeamId);
