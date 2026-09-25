using System.Collections.Concurrent;

namespace Foundation.Core.Chatbot.Features;

/// <summary>
/// Stockage en mémoire, à durée de vie courte, des menus de désambiguïsation en attente, indexés par
/// (room, expéditeur). Permet à une commande de demander « lequel de ces N vouliez-vous ? » et de
/// résoudre la prochaine réponse purement numérique du même utilisateur dans la même room, sans que
/// le bot ait par ailleurs le moindre état conversationnel.
/// </summary>
public sealed class PendingDisambiguationStore
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<(string RoomId, string UserId), PendingDisambiguation> _pending = new();

    public int Count => _pending.Count;

    public void Set(string roomId, string userId, PendingDisambiguation entry) =>
        _pending[(roomId, userId)] = entry;

    /// <summary>
    /// Retire et retourne toutes les entrées dont la durée de vie est écoulée, pour que l'appelant
    /// puisse clore activement ces demandes (message d'expiration) au lieu de les laisser mourir en
    /// silence.
    /// </summary>
    public IReadOnlyList<(string RoomId, string UserId)> RemoveExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = new List<(string RoomId, string UserId)>();
        foreach (var kvp in _pending)
        {
            if (kvp.Value.ExpiresAt <= now && _pending.TryRemove(kvp.Key, out _))
            {
                expired.Add(kvp.Key);
            }
        }

        return expired;
    }

    /// <summary>
    /// Retire et retourne toutes les entrées restantes, quelle que soit leur échéance. Utilisé à
    /// l'arrêt du bot : les closures capturent des services (HttpClient, passerelle métier) qui sont
    /// reconstruits au rechargement des options, donc un menu survivant pointerait vers des objets
    /// libérés.
    /// </summary>
    public IReadOnlyList<(string RoomId, string UserId)> RemoveAll()
    {
        var keys = _pending.Keys.ToList();
        foreach (var key in keys)
        {
            _pending.TryRemove(key, out _);
        }

        return keys;
    }

    /// <summary>Retourne l'entrée en attente sans la consommer (une réponse hors bornes ne doit pas détruire le menu).</summary>
    public bool TryPeek(string roomId, string userId, out PendingDisambiguation entry)
    {
        if (_pending.TryGetValue((roomId, userId), out var found) && found.ExpiresAt > DateTimeOffset.UtcNow)
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }

    public void Remove(string roomId, string userId) => _pending.TryRemove((roomId, userId), out _);
}
