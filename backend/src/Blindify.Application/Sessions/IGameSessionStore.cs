using Blindify.Domain.Entities;

namespace Blindify.Application.Sessions;

/// <summary>Parties actives en mémoire — pas de base de données, voir CLAUDE.md.</summary>
public interface IGameSessionStore
{
    void Add(GameSession session);
    GameSession? Get(string code);
    bool Exists(string code);
    void Remove(string code);

    /// <summary>Instantané de toutes les sessions — purge des salons abandonnés (SessionCleanupService).</summary>
    IReadOnlyList<GameSession> Lister();

    /// <summary>
    /// Les méthodes host/joueur du contrat SignalR (StartRound, SubmitAnswer, ...) ne prennent pas de
    /// `code` en paramètre — la partie est retrouvée à partir du ConnectionId de l'appelant.
    /// </summary>
    void AssocierConnexion(string connectionId, string code);

    string? ObtenirCodeParConnexion(string connectionId);

    void DissocierConnexion(string connectionId);
}
