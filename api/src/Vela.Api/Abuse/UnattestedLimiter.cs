using System.Threading.RateLimiting;

namespace Vela.Api.Abuse;

/// <summary>
/// Owner decision 2026-11-03: protected requests that pass without a verified attestation (iOS at launch, whose claim cannot be
/// proven, or any platform whose switch is off) get a tighter budget per client address and action (per account for
/// <c>create_work</c>). In memory per instance, like the other limiters. Configure with
/// <c>Abuse:RateLimits:unattested-&lt;action&gt;:Permit</c>; Development and Test default to a very high permit.
/// </summary>
public sealed class UnattestedLimiter : IDisposable
{
    public const string PolicyPrefix = "unattested-";
    private const int LenientPermit = 100_000;

    /// <summary>Launch defaults; compare with the attested budgets (auth-email 20/h per IP, signup velocity 5/day per IP, upload 20/h per user).</summary>
    public static readonly IReadOnlyDictionary<string, (int Permit, TimeSpan Window)> Defaults = new Dictionary<string, (int, TimeSpan)>
    {
        ["email_request"] = (5, TimeSpan.FromHours(1)),
        ["sso"] = (10, TimeSpan.FromHours(1)),
        ["create_work"] = (10, TimeSpan.FromDays(1)),
    };

    private readonly PartitionedRateLimiter<(string Action, string Subject)> _limiter;

    public UnattestedLimiter(AbuseOptions options, IHostEnvironment env)
    {
        var lenient = env.IsDevelopment() || env.IsEnvironment("Test");
        _limiter = PartitionedRateLimiter.Create<(string Action, string Subject), string>(key =>
        {
            var (permit, window) = Defaults.TryGetValue(key.Action, out var d) ? d : (5, TimeSpan.FromHours(1));
            if (lenient) permit = LenientPermit;
            if (options.PermitOverrides.TryGetValue(PolicyPrefix + key.Action, out var configured)) permit = configured;
            return RateLimitPartition.GetFixedWindowLimiter($"{key.Action}|{key.Subject}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permit, Window = window, QueueLimit = 0, AutoReplenishment = true,
            });
        });
    }

    /// <summary>Null when allowed; otherwise how long to wait.</summary>
    public TimeSpan? TryAcquire(string action, string subject)
    {
        using var lease = _limiter.AttemptAcquire((action, subject));
        if (lease.IsAcquired) return null;
        return lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromMinutes(1);
    }

    public void Dispose() => _limiter.Dispose();
}
