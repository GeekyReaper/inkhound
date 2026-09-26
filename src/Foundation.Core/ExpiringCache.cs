using System.Collections.Concurrent;
using Foundation.Core.Interface;

namespace Foundation.Core;

/// <summary>
/// Cache mémoire borné par TTL <b>et</b> par nombre d'entrées, thread-safe.
/// </summary>
/// <remarks>
/// Écrit sans <c>IMemoryCache</c> : Foundation.Core ne prend aucune dépendance NuGet (voir son
/// CLAUDE.md). Il remplace les <c>ConcurrentDictionary</c> nus qui servaient de caches dans le
/// domaine et ne relâchaient jamais rien — soit parce qu'ils n'avaient aucune éviction, soit parce
/// que leur TTL n'était vérifié qu'à la lecture, ce qui laisse vivre indéfiniment une entrée plus
/// jamais relue.
///
/// Deux points de purge complémentaires : paresseuse (à chaque écriture, plus le contrôle
/// d'expiration à la lecture) et périodique via <see cref="PurgeExpiredEntries"/>, que
/// <see cref="BaseServiceManager"/> appelle depuis sa boucle de monitoring pour les caches qui lui
/// sont enregistrés — c'est le second qui garantit qu'un cache inactif finit par se vider.
/// </remarks>
public sealed class ExpiringCache<TKey, TValue>(string name, TimeSpan ttl, int maxEntries) : IPurgeableCache
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Entry> _entries = new();
    private readonly TimeSpan _ttl = ttl > TimeSpan.Zero ? ttl : TimeSpan.FromMinutes(15);
    private readonly int _maxEntries = maxEntries > 0 ? maxEntries : 128;

    private sealed record Entry(TValue Value, DateTime StoredAtUtc);

    /// <summary>Nom lisible, exposé par l'API d'observation mémoire.</summary>
    public string CacheName { get; } = name;

    public int CachedEntryCount => _entries.Count;

    public bool TryGet(TKey key, out TValue? value)
    {
        value = default;

        if (!_entries.TryGetValue(key, out var entry))
            return false;

        if (IsExpired(entry))
        {
            _entries.TryRemove(key, out _);
            return false;
        }

        value = entry.Value;
        return true;
    }

    public void Set(TKey key, TValue value)
    {
        _entries[key] = new Entry(value, DateTime.UtcNow);
        PurgeExpiredEntries();
        EvictOldestOverflow();
    }

    public bool Remove(TKey key) => _entries.TryRemove(key, out _);

    /// <summary>Vide le cache et retourne le nombre d'entrées supprimées.</summary>
    public int PurgeCache()
    {
        var removed = _entries.Count;
        _entries.Clear();
        return removed;
    }

    /// <summary>Retire les entrées expirées et retourne leur nombre.</summary>
    public int PurgeExpiredEntries()
    {
        var removed = 0;
        foreach (var pair in _entries)
        {
            if (IsExpired(pair.Value) && _entries.TryRemove(pair.Key, out _))
                removed++;
        }
        return removed;
    }

    private bool IsExpired(Entry entry) => DateTime.UtcNow - entry.StoredAtUtc > _ttl;

    // Éviction de la plus ancienne entrée tant qu'on dépasse la borne. Boucle bornée : chaque tour
    // retire une entrée, et un retrait concurrent fait sortir au tour suivant.
    private void EvictOldestOverflow()
    {
        while (_entries.Count > _maxEntries)
        {
            var oldest = default(KeyValuePair<TKey, Entry>);
            var found = false;

            foreach (var pair in _entries)
            {
                if (!found || pair.Value.StoredAtUtc < oldest.Value.StoredAtUtc)
                {
                    oldest = pair;
                    found = true;
                }
            }

            if (!found || !_entries.TryRemove(oldest.Key, out _))
                return;
        }
    }
}
