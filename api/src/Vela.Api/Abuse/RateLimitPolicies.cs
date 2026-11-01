using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Vela.Api.Common;

namespace Vela.Api.Abuse;

/// <summary>
/// Per-policy request limits (in memory, per instance; idle partitions are discarded by the framework so keys cannot pile up).
/// Every endpoint falls under a global classification: anonymous reads are `anon-read`, writes are `write`. Specific
/// endpoints add a named policy on top with <c>RequireRateLimiting</c>.
/// </summary>
public static class RateLimitPolicies
{
    public const string AuthEmail = "auth-email";
    public const string AnonRead = "anon-read";
    public const string Write = "write";
    public const string Upload = "upload";
    public const string PublicForm = "public-form";
    public const string UserRead = "user-read";

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly string[] Unlimited = ["/health"];

    private static readonly Dictionary<string, (int Permit, TimeSpan Window)> Defaults = new()
    {
        [AuthEmail] = (20, Hour),   // per IP; the per-email 5/hour limit lives with the code store (EmailCodeService)
        [AnonRead] = (120, Minute),
        [Write] = (60, Minute),
        [Upload] = (20, Hour),
        [PublicForm] = (5, Hour),
        [UserRead] = (600, Minute), // generous: a signed-in scraper is slowed, a person scrolling never notices
    };

    public static IServiceCollection AddAbuseRateLimiting(this IServiceCollection services, AbuseOptions abuse) =>
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = RejectAsync;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => Classify(ctx, abuse));
            o.AddPolicy(AuthEmail, ctx => Partition(AuthEmail, "ip:" + IpOf(ctx), abuse));
            o.AddPolicy(PublicForm, ctx => Partition(PublicForm, "ip:" + IpOf(ctx), abuse));
            o.AddPolicy(Upload, ctx => Partition(Upload, SubjectOf(ctx), abuse));
        });

    public static TBuilder RequireAuthEmailLimit<TBuilder>(this TBuilder b) where TBuilder : IEndpointConventionBuilder => b.RequireRateLimiting(AuthEmail);
    public static TBuilder RequirePublicFormLimit<TBuilder>(this TBuilder b) where TBuilder : IEndpointConventionBuilder => b.RequireRateLimiting(PublicForm);
    public static TBuilder RequireUploadLimit<TBuilder>(this TBuilder b) where TBuilder : IEndpointConventionBuilder => b.RequireRateLimiting(Upload);

    private static RateLimitPartition<string> Classify(HttpContext ctx, AbuseOptions abuse)
    {
        var path = ctx.Request.Path;
        if (Unlimited.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
            return RateLimitPartition.GetNoLimiter("unlimited");

        var read = HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method) || HttpMethods.IsOptions(ctx.Request.Method);
        if (read) return IsSignedIn(ctx) ? Partition(UserRead, SubjectOf(ctx), abuse) : Partition(AnonRead, "ip:" + IpOf(ctx), abuse);
        return Partition(Write, SubjectOf(ctx), abuse);
    }

    private static RateLimitPartition<string> Partition(string policy, string subject, AbuseOptions abuse)
    {
        var (permit, window) = Defaults[policy];
        if (abuse.PermitOverrides.TryGetValue(policy, out var configured)) permit = configured;
        // The subject is a user id or a normalised address bucket: bounded by real traffic, and dropped once idle.
        return RateLimitPartition.GetFixedWindowLimiter($"{policy}|{subject}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permit, Window = window, QueueLimit = 0, AutoReplenishment = true,
        });
    }

    private static bool IsSignedIn(HttpContext ctx) => ctx.User.Identity?.IsAuthenticated == true && ctx.User.FindFirst("sub") is not null;

    private static string SubjectOf(HttpContext ctx) =>
        IsSignedIn(ctx) ? "user:" + ctx.User.FindFirst("sub")!.Value : "ip:" + IpOf(ctx);

    private static string IpOf(HttpContext ctx) => IpKey.Of(ctx.RequestServices.GetRequiredService<IClientIpResolver>().Resolve(ctx));

    private static ValueTask RejectAsync(OnRejectedContext context, CancellationToken ct)
    {
        var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? (int)Math.Ceiling(wait.TotalSeconds) : 60;
        context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, seconds).ToString(CultureInfo.InvariantCulture);
        return new ValueTask(Results.Fail(StatusCodes.Status429TooManyRequests, "rate_limited", "Too many requests. Try again later.")
            .ExecuteAsync(context.HttpContext));
    }
}
