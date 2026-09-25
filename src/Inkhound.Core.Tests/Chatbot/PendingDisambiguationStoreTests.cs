using Foundation.Core.Chatbot.Features;

namespace Inkhound.Core.Tests.Chatbot;

public class PendingDisambiguationStoreTests
{
    private const string Room = "!room:server";
    private const string User = "@user:server";

    private static PendingDisambiguation Entry(int candidates = 3, TimeSpan? ttl = null) => new(
        CandidateCount: candidates,
        ResolveAsync: (_, _) => Task.FromResult(FeatureExecutionResult.Ok("ok")),
        ExpiresAt: DateTimeOffset.UtcNow + (ttl ?? TimeSpan.FromMinutes(10)));

    [Fact]
    public void TryPeek_does_not_consume_the_entry()
    {
        // Une réponse hors bornes ne doit pas détruire le menu : l'utilisateur doit pouvoir
        // ressaisir un numéro valide.
        var store = new PendingDisambiguationStore();
        store.Set(Room, User, Entry());

        Assert.True(store.TryPeek(Room, User, out _));
        Assert.True(store.TryPeek(Room, User, out var second));
        Assert.Equal(3, second.CandidateCount);
    }

    [Fact]
    public void TryPeek_ignores_an_expired_entry()
    {
        var store = new PendingDisambiguationStore();
        store.Set(Room, User, Entry(ttl: TimeSpan.FromMilliseconds(-1)));

        Assert.False(store.TryPeek(Room, User, out _));
    }

    [Fact]
    public void Remove_clears_the_entry()
    {
        var store = new PendingDisambiguationStore();
        store.Set(Room, User, Entry());
        store.Remove(Room, User);

        Assert.False(store.TryPeek(Room, User, out _));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void RemoveExpired_returns_only_expired_entries()
    {
        var store = new PendingDisambiguationStore();
        store.Set(Room, "@old:server", Entry(ttl: TimeSpan.FromMilliseconds(-1)));
        store.Set(Room, "@fresh:server", Entry());

        var expired = store.RemoveExpired();

        Assert.Equal([(Room, "@old:server")], expired);
        Assert.Equal(1, store.Count);
        Assert.True(store.TryPeek(Room, "@fresh:server", out _));
    }

    [Fact]
    public void RemoveAll_drops_every_entry_regardless_of_expiry()
    {
        // Appelé à l'arrêt du bot : les closures capturent des services reconstruits au prochain
        // chargement d'options, un menu survivant pointerait vers des objets libérés.
        var store = new PendingDisambiguationStore();
        store.Set(Room, "@a:server", Entry());
        store.Set(Room, "@b:server", Entry());

        var removed = store.RemoveAll();

        Assert.Equal(2, removed.Count);
        Assert.Equal(0, store.Count);
    }
}
