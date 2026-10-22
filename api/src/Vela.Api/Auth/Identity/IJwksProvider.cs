using Microsoft.IdentityModel.Tokens;

namespace Vela.Api.Auth.Identity;

public interface IJwksProvider
{
    /// <summary>Returns the signing key with this kid, or null when the provider does not publish it.</summary>
    Task<SecurityKey?> GetKeyAsync(string jwksUri, string kid, CancellationToken ct = default);
}

/// <summary>Fetches provider JWKS over HTTP, caches per URI, and refreshes on an unknown kid (rate limited).</summary>
public sealed class HttpJwksProvider(HttpClient http, TimeProvider clock) : IJwksProvider
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);
    private static readonly TimeSpan MinRefresh = TimeSpan.FromMinutes(1);
    private readonly Dictionary<string, (DateTimeOffset At, Dictionary<string, SecurityKey> Keys)> _cache = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<SecurityKey?> GetKeyAsync(string jwksUri, string kid, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = clock.GetUtcNow();
            if (_cache.TryGetValue(jwksUri, out var hit))
            {
                if (hit.Keys.TryGetValue(kid, out var cached) && now - hit.At < MaxAge) return cached;
                if (now - hit.At < MinRefresh) return null; // unknown kid: do not hammer the provider
            }
            var json = await http.GetStringAsync(jwksUri, ct);
            var keys = new JsonWebKeySet(json).GetSigningKeys()
                .Where(k => k.KeyId is not null).ToDictionary(k => k.KeyId!, k => k);
            _cache[jwksUri] = (now, keys);
            return keys.GetValueOrDefault(kid);
        }
        finally { _gate.Release(); }
    }
}
