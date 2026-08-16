using System.Collections.Concurrent;
using Blindify.Domain.Entities;

namespace Blindify.Application.Sessions;

public class GameSessionStore : IGameSessionStore
{
    private readonly ConcurrentDictionary<string, GameSession> _sessions = new();
    private readonly ConcurrentDictionary<string, string> _connexions = new();

    public void Add(GameSession session) => _sessions[session.Id] = session;

    public GameSession? Get(string code) => _sessions.GetValueOrDefault(code);

    public bool Exists(string code) => _sessions.ContainsKey(code);

    public void Remove(string code) => _sessions.TryRemove(code, out _);

    public void AssocierConnexion(string connectionId, string code) => _connexions[connectionId] = code;

    public string? ObtenirCodeParConnexion(string connectionId) => _connexions.GetValueOrDefault(connectionId);

    public void DissocierConnexion(string connectionId) => _connexions.TryRemove(connectionId, out _);
}
