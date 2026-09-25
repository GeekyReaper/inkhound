using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Foundation.Core.Chatbot.Vision;

/// <summary>
/// Cache best-effort des résultats d'analyse, en mémoire par process (même logique « pas de base de
/// données » que <see cref="UsageStatisticsTracker"/> : approximatif, repart de zéro au redémarrage).
/// </summary>
/// <remarks>
/// Réécrit sans <c>IMemoryCache</c> : Foundation.Core ne prend aucune dépendance NuGet. La sémantique
/// de clé de l'original DocuMind est conservée — SHA-256 du quadruplet (image, mediaType, prompt,
/// provider résolu), le provider en faisant partie car deux providers ne doivent pas partager un
/// résultat pour la même image. Seuls les résultats réussis sont mis en cache.
/// </remarks>
public sealed class VisionAnalysisCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    private volatile bool _enabled = true;
    private TimeSpan _ttl = TimeSpan.FromHours(1);
    private int _maxEntries = DefaultMaxEntries;

    // Une couverture pèse typiquement quelques centaines de Ko : 64 entrées bornent l'empreinte
    // mémoire à quelques dizaines de Mo dans le pire cas.
    private const int DefaultMaxEntries = 64;

    private sealed record CacheEntry(DateTimeOffset ExpiresAt, DateTimeOffset StoredAt, VisionResult Result);

    public void Configure(bool enabled, TimeSpan ttl, int maxEntries = DefaultMaxEntries)
    {
        _enabled = enabled;
        _ttl = ttl > TimeSpan.Zero ? ttl : TimeSpan.FromHours(1);
        _maxEntries = maxEntries > 0 ? maxEntries : DefaultMaxEntries;

        if (!enabled)
        {
            Clear();
        }
    }

    public bool TryGet(byte[] content, string mediaType, string prompt, string providerName, out VisionResult? result)
    {
        result = null;
        if (!_enabled) return false;

        PurgeExpired();

        var key = BuildKey(content, mediaType, prompt, providerName);
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > DateTimeOffset.UtcNow)
            {
                result = entry.Result with { FromCache = true };
                return true;
            }

            _entries.TryRemove(key, out _);
        }

        return false;
    }

    public void Set(byte[] content, string mediaType, string prompt, string providerName, VisionResult result)
    {
        if (!_enabled || !result.Success) return;

        var now = DateTimeOffset.UtcNow;
        var key = BuildKey(content, mediaType, prompt, providerName);
        _entries[key] = new CacheEntry(now.Add(_ttl), now, result);

        PurgeExpired();
        EvictOldestOverflow();
    }

    public void Clear() => _entries.Clear();

    public int Count => _entries.Count;

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }
    }

    // Éviction de la plus ancienne entrée tant qu'on dépasse la borne. Boucle bornée : chaque tour
    // retire une entrée, et un retrait concurrent fait simplement sortir de la boucle au tour suivant.
    private void EvictOldestOverflow()
    {
        while (_entries.Count > _maxEntries)
        {
            var oldest = _entries.OrderBy(e => e.Value.StoredAt).FirstOrDefault();
            if (oldest.Key is null || !_entries.TryRemove(oldest.Key, out _))
            {
                return;
            }
        }
    }

    private static string BuildKey(byte[] content, string mediaType, string prompt, string providerName)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(content);
        hasher.AppendData(Encoding.UTF8.GetBytes(mediaType));
        hasher.AppendData(Encoding.UTF8.GetBytes(prompt));
        hasher.AppendData(Encoding.UTF8.GetBytes(providerName));
        return Convert.ToHexString(hasher.GetHashAndReset());
    }
}
