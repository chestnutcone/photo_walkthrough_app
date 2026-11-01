namespace Vela.Api.Common;

public sealed class RateLimitedException(TimeSpan retryAfter) : Exception("Too many requests.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>Sliding-window helpers for DB-backed limits. Callers pass the timestamps already in the window.</summary>
public static class RateLimiting
{
    /// <summary>Returns null when allowed, otherwise how long until the oldest hit leaves the window.</summary>
    public static TimeSpan? RetryAfter(IReadOnlyList<DateTimeOffset> hitsInWindow, int limit, TimeSpan window, DateTimeOffset now)
    {
        if (hitsInWindow.Count < limit) return null;
        var oldestCounting = hitsInWindow.OrderByDescending(t => t).ElementAt(limit - 1);
        var wait = oldestCounting + window - now;
        return wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait;
    }
}
