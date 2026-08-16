using Blindify.Application.Sessions;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;

namespace Blindify.Tests.Sessions;

public class GameSessionStoreTests
{
    private readonly GameSessionStore _store = new();

    private static GameSession NouvelleSession(string id) => new() { Id = id, Config = new GameConfig() };

    [Fact]
    public void Add_PuisGet_RetrouveLaSession()
    {
        var session = NouvelleSession("ABCDE");
        _store.Add(session);

        Assert.Same(session, _store.Get("ABCDE"));
    }

    [Fact]
    public void Get_CodeInconnu_RetourneNull()
    {
        Assert.Null(_store.Get("INCONNU"));
    }

    [Fact]
    public void Exists_ReconnaitUneSessionAjoutee()
    {
        _store.Add(NouvelleSession("ABCDE"));

        Assert.True(_store.Exists("ABCDE"));
        Assert.False(_store.Exists("AUTRE"));
    }

    [Fact]
    public void Remove_SupprimeLaSession()
    {
        _store.Add(NouvelleSession("ABCDE"));
        _store.Remove("ABCDE");

        Assert.Null(_store.Get("ABCDE"));
    }
}
