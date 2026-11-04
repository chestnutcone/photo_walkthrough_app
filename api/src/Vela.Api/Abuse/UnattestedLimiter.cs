using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Vela.Api.Common;
using Vela.Api.Telemetry;

namespace Vela.Api.Abuse;

/// <summary>
/// Owner decision 2026-11-03: protected requests that pass without a verified attestation (iOS at launch, whose claim cannot be
/// proven, or any platform whose switch is off) get their own budget per client address and action (per account for
/// <c>create_work</c>). In memory per instance, like the other limiters. Configure with
/// <c>Abuse:RateLimits:unattested-&lt;action&gt;:Permit</c>; Development and Test default to a very high permit.
/// <para>
/// DECISIONS fix I-1: one address is often shared by many real people (carrier CGNAT, school, office, event wifi), so the numbers
/// are sized for a crowded address, not for one phone, and only requests that pass basic validation spend them: the endpoints
/// charge <c>sso</c> (after the identity token verifies) and <c>email_request</c>/<c>email_target</c> (after the address is a
/// valid, permanent email); the middleware charges <c>create_work</c> per account. Every refusal is counted by
/// <c>vela.abuse.unattested_limited{action}</c> so the owner can tune the permits after launch. The budget bounds abuse per
/// address only; a proxy pool scales it (threat model R-3).
/// </para>
/// </summary>
public sealed class UnattestedLimiter : IDisposable
{
    public const string PolicyPrefix = "unattested-";
    public const string EmailRequest = "email_request";
    public const string EmailTarget = "email_target";
    public const string Sso = "sso";
    public const string CreateWork = "create_work";
    private const int LenientPermit = 100_000;
    private static readonly object Marker = new();

    /// <summary>Launch defaults. Attested budgets for comparison: auth-email 20/h per IP and 5/h per email (all platforms), signup velocity 5/day per IP, upload 20/h per user.</summary>
    public static readonly IReadOnlyDictionary<string, (int Permit, TimeSpan Window)> Defaults = new Dictionary<string, (int, TimeSpan)>
    {
        [EmailRequest] = (20, TimeSpan.FromHours(1)), // per address
        [EmailTarget] = (5, TimeSpan.FromHours(1)),   // per (address, target email)
        [Sso] = (60, TimeSpan.FromHours(1)),          // per address, verified sign-ins only
        [CreateWork] = (10, TimeSpan.FromDays(1)),    // per account
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

    /// <summary>Spends one permit. Null when allowed; otherwise how long to wait (and the refusal is counted).</summary>
    public TimeSpan? TryAcquire(string action, string subject) => Attempt(action, subject, 1);

    /// <summary>Checks that a permit is left without spending it, so work that may fail validation is not done for an exhausted subject.</summary>
    public TimeSpan? Peek(string action, string subject) => Attempt(action, subject, 0);

    private TimeSpan? Attempt(string action, string subject, int permits)
    {
        using var lease = _limiter.AttemptAcquire((action, subject), permits);
        if (lease.IsAcquired) return null;
        if (permits > 0) VelaMetrics.UnattestedLimited(action);
        return lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromMinutes(1);
    }

    /// <summary>Set by <see cref="AttestationMiddleware"/> when a request passes without a verified attestation.</summary>
    public static void MarkUnattested(HttpContext ctx) => ctx.Items[Marker] = true;
    public static bool IsUnattested(HttpContext ctx) => ctx.Items.ContainsKey(Marker);

    public static string AddressSubject(HttpContext ctx, IClientIpResolver ips) => "ip:" + IpKey.Of(ips.Resolve(ctx));

    /// <summary>(address, target email) key; the email is hashed so the in-memory partitions hold no address.</summary>
    public static string TargetSubject(string addressSubject, string normalizedEmail) =>
        addressSubject + "|email:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail)))[..32];

    /// <summary>
    /// Email path, for an unattested request with a valid, permanent address: the (address, email) key is checked first so a
    /// burst at one email cannot spend the address budget everyone behind the address shares; then the address key.
    /// </summary>
    public TimeSpan? TryAcquireEmail(HttpContext ctx, IClientIpResolver ips, string normalizedEmail)
    {
        var address = AddressSubject(ctx, ips);
        return TryAcquire(EmailTarget, TargetSubject(address, normalizedEmail)) ?? TryAcquire(EmailRequest, address);
    }

    public static IResult Refuse(HttpContext ctx, TimeSpan wait)
    {
        ctx.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        return Results.Fail(StatusCodes.Status429TooManyRequests, "rate_limited", "Too many requests. Try again later.");
    }

    public void Dispose() => _limiter.Dispose();
}
