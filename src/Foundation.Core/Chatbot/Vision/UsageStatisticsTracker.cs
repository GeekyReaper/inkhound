namespace Foundation.Core.Chatbot.Vision;

/// <summary>
/// Suivi local (en mémoire, par process) des requêtes/tokens consommés, avec fenêtres glissantes
/// minute/jour. Sert à estimer un quota restant pour les providers qui ne le renvoient pas
/// eux-mêmes ; approximatif, car il ignore les autres process/clients utilisant la même clé API et
/// repart de zéro à chaque redémarrage.
/// </summary>
public sealed class UsageStatisticsTracker
{
    private readonly object _lock = new();
    private readonly Queue<UsageEntry> _entries = new();

    private UsageLimits _limits = new();

    /// <summary>Recharge les limites déclarées sans perdre l'historique déjà accumulé.</summary>
    public void SetLimits(UsageLimits limits)
    {
        lock (_lock)
        {
            _limits = limits;
        }
    }

    public void Record(TokenUsage usage)
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            _entries.Enqueue(new UsageEntry(now, usage.TotalTokens));
            Prune(now);
        }
    }

    public UsageStatisticsSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            Prune(now);

            var minuteCutoff = now.AddMinutes(-1);
            var lastMinute = _entries.Where(e => e.Timestamp > minuteCutoff).ToList();

            var requestsLastMinute = lastMinute.Count;
            var tokensLastMinute = lastMinute.Sum(e => e.Tokens);
            var requestsLastDay = _entries.Count;
            var tokensLastDay = _entries.Sum(e => e.Tokens);

            return new UsageStatisticsSnapshot(
                RequestsLastMinute: requestsLastMinute,
                TokensLastMinute: tokensLastMinute,
                RequestsLastDay: requestsLastDay,
                TokensLastDay: tokensLastDay,
                RequestsPerMinuteRemaining: _limits.RequestsPerMinute - requestsLastMinute,
                TokensPerMinuteRemaining: _limits.TokensPerMinute - tokensLastMinute,
                RequestsPerDayRemaining: _limits.RequestsPerDay - requestsLastDay,
                TokensPerDayRemaining: _limits.TokensPerDay - tokensLastDay);
        }
    }

    private void Prune(DateTimeOffset now)
    {
        var dayCutoff = now.AddDays(-1);
        while (_entries.Count > 0 && _entries.Peek().Timestamp <= dayCutoff)
        {
            _entries.Dequeue();
        }
    }

    private readonly record struct UsageEntry(DateTimeOffset Timestamp, int Tokens);
}
