using Microsoft.IdentityModel.Tokens;

namespace Vela.Api.Auth.Identity;

public interface IJwksProvider
{
    /// <summary>Returns the signing key with this kid, or null when the provider does not publish it.</summary>
    Task<SecurityKey?> GetKeyAsync(string jwksUri, string kid, CancellationToken ct = default);
}

/// <summary>
/// Fetches provider JWKS over HTTP, caches per URI, and refreshes on an unknown kid (rate limited). A failed fetch also starts
/// the cool-down, so a provider outage costs one attempt per minute instead of one per sign-in; keys already cached keep working
/// meanwhile. Each URI has its own lock, so a slow or failing provider never blocks the other.
/// </summary>
public sealed class HttpJwksProvider(HttpClient http, TimeProvider clock) : IJwksProvider
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);
    private static readonly TimeSpan MinRefresh = TimeSpan.FromMinutes(1);
    private sealed record Entry(DateTimeOffset At, DateTimeOffset LastAttempt, Dictionary<string, SecurityKey> Keys);
    private readonly Dictionary<string, Entry> _cache = [];
    private readonly Dictionary<string, SemaphoreSlim> _gates = [];

    public async Task<SecurityKey?> GetKeyAsync(string jwksUri, string kid, CancellationToken ct = default)
    {
        var gate = GateFor(jwksUri);
        await gate.WaitAsync(ct);
        try
        {
            var now = clock.GetUtcNow();
            if (_cache.TryGetValue(jwksUri, out var hit))
            {
                if (hit.Keys.TryGetValue(kid, out var cached) && now - hit.At < MaxAge) return cached;
                if (now - hit.LastAttempt < MinRefresh) return null; // unknown kid or provider down: do not hammer it
            }
            try
            {
                var json = await http.GetStringAsync(jwksUri, ct);
                // A provider listing the same kid twice must not take sign-in down: the last entry wins.
                var keys = new JsonWebKeySet(json).GetSigningKeys().Where(k => k.KeyId is not null)
                    .GroupBy(k => k.KeyId!).ToDictionary(g => g.Key, g => g.Last());
                _cache[jwksUri] = new Entry(now, now, keys);
                return keys.GetValueOrDefault(kid);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or ArgumentException && !ct.IsCancellationRequested)
            {
                // Remember the attempt; keep whatever keys we had (they still verify tokens signed by them).
                _cache[jwksUri] = new Entry(hit?.At ?? DateTimeOffset.MinValue, now, hit?.Keys ?? []);
                throw;
            }
        }
        finally { gate.Release(); }
    }

    private SemaphoreSlim GateFor(string uri)
    {
        lock (_gates) return _gates.TryGetValue(uri, out var g) ? g : _gates[uri] = new SemaphoreSlim(1, 1);
    }
}
