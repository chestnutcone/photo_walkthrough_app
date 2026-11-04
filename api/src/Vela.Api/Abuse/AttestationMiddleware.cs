using Vela.Api.Common;

namespace Vela.Api.Abuse;

/// <summary>Endpoint metadata: this operation needs an attestation token minted for <see cref="Action"/> when attestation is required.</summary>
public sealed record AttestationRequirement(string Action);

public static class AttestationEndpointExtensions
{
    public static TBuilder RequireAttestation<TBuilder>(this TBuilder b, string action) where TBuilder : IEndpointConventionBuilder =>
        b.WithMetadata(new AttestationRequirement(action));
}

/// <summary>
/// Per-platform attestation (owner decision 2026-11-03). Only a request carrying exactly one <c>X-Platform: ios</c> header gets the
/// iOS rule; anything else (no header, android, web, unknown, several values) gets the Android rule, so a request cannot reach a
/// weaker rule by omitting or garbling the header. Where the rule requires attestation it fails closed: anything short of a
/// verified token (including a verifier error) is a 403. Where it does not, the request passes but is held to the tighter
/// <see cref="UnattestedLimiter"/> budget, which is all an unprovable iOS claim can buy: create_work is charged here, sso and
/// email_request by their endpoints after basic validation (the request is marked with <see cref="UnattestedLimiter.MarkUnattested"/>).
/// </summary>
public sealed class AttestationMiddleware(RequestDelegate next, AbuseOptions options, UnattestedLimiter unattested,
    IClientIpResolver ips, ILogger<AttestationMiddleware> log)
{
    public const string TokenHeader = "X-Attestation";
    public const string PlatformHeader = "X-Platform";
    private const int MaxTokenLength = 32 * 1024;

    public async Task InvokeAsync(HttpContext ctx, IAttestationVerifier verifier)
    {
        if (ctx.GetEndpoint()?.Metadata.GetMetadata<AttestationRequirement>() is not { } requirement)
        {
            await next(ctx);
            return;
        }
        var platform = ClaimedPlatform(ctx);
        var required = platform == "ios" ? options.RequireAttestationIos : options.RequireAttestationAndroid;
        if (!required)
        {
            // sso and email_request are charged by their endpoints once the request passes basic validation (DECISIONS I-1).
            if (requirement.Action == UnattestedLimiter.CreateWork
                && unattested.TryAcquire(requirement.Action, SubjectOf(ctx)) is { } wait)
            {
                await UnattestedLimiter.Refuse(ctx, wait).ExecuteAsync(ctx);
                return;
            }
            UnattestedLimiter.MarkUnattested(ctx);
            await next(ctx);
            return;
        }
        await EnforceAsync(ctx, verifier, platform, requirement.Action);
    }

    private async Task EnforceAsync(HttpContext ctx, IAttestationVerifier verifier, string platform, string action)
    {
        var token = ctx.Request.Headers[TokenHeader].ToString();
        if (token.Length == 0)
        {
            await Deny("attestation_required", "This request needs an app attestation.").ExecuteAsync(ctx);
            return;
        }
        if (!await VerifiedAsync(ctx, verifier, platform, token, action))
        {
            await Deny("attestation_failed", "The app attestation could not be verified.").ExecuteAsync(ctx);
            return;
        }
        await next(ctx);
    }

    /// <summary>"ios" only for a single, exact (trimmed, case-insensitive) ios value; otherwise the strict default "android".</summary>
    public static string ClaimedPlatform(HttpContext ctx)
    {
        var values = ctx.Request.Headers[PlatformHeader];
        return values.Count == 1 && string.Equals(values[0]?.Trim(), "ios", StringComparison.OrdinalIgnoreCase) ? "ios" : "android";
    }

    private async Task<bool> VerifiedAsync(HttpContext ctx, IAttestationVerifier verifier, string platform, string token, string action)
    {
        // The Android rule still wants the header to say android: a token is verified only for the platform the app declared.
        var declared = ctx.Request.Headers[PlatformHeader].ToString().Trim().ToLowerInvariant();
        if (declared != platform || token.Length > MaxTokenLength) return false;
        try { return await verifier.VerifyAsync(platform, token, action); }
        catch (Exception e)
        {
            log.LogWarning("Attestation verifier failed. action={Action} reason={Reason}", action, e.GetType().Name);
            return false;
        }
    }

    /// <summary>Signed-in uploads count per account (the address bucket only for a request without a token, which authorization then refuses).</summary>
    private string SubjectOf(HttpContext ctx) =>
        ctx.User.FindFirst("sub") is { } sub ? "user:" + sub.Value : UnattestedLimiter.AddressSubject(ctx, ips);

    private static IResult Deny(string code, string message) => Results.Fail(StatusCodes.Status403Forbidden, code, message);
}
