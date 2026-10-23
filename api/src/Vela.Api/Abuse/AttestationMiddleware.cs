using Vela.Api.Common;

namespace Vela.Api.Abuse;

/// <summary>Endpoint metadata: this operation needs an attestation token minted for <see cref="Action"/> when attestation is required.</summary>
public sealed record AttestationRequirement(string Action);

public static class AttestationEndpointExtensions
{
    public static TBuilder RequireAttestation<TBuilder>(this TBuilder b, string action) where TBuilder : IEndpointConventionBuilder =>
        b.WithMetadata(new AttestationRequirement(action));
}

/// <summary>Fails closed: when attestation is required, anything short of a verified token (including a verifier error) is a 403.</summary>
public sealed class AttestationMiddleware(RequestDelegate next, AbuseOptions options, ILogger<AttestationMiddleware> log)
{
    public const string TokenHeader = "X-Attestation";
    public const string PlatformHeader = "X-Platform";
    private const int MaxTokenLength = 32 * 1024;

    public async Task InvokeAsync(HttpContext ctx, IAttestationVerifier verifier)
    {
        if (!options.RequireAttestation || ctx.GetEndpoint()?.Metadata.GetMetadata<AttestationRequirement>() is not { } requirement)
        {
            await next(ctx);
            return;
        }

        var token = ctx.Request.Headers[TokenHeader].ToString();
        if (token.Length == 0)
        {
            await Deny("attestation_required", "This request needs an app attestation.").ExecuteAsync(ctx);
            return;
        }
        if (!await VerifiedAsync(ctx, verifier, token, requirement.Action))
        {
            await Deny("attestation_failed", "The app attestation could not be verified.").ExecuteAsync(ctx);
            return;
        }
        await next(ctx);
    }

    private async Task<bool> VerifiedAsync(HttpContext ctx, IAttestationVerifier verifier, string token, string action)
    {
        var platform = ctx.Request.Headers[PlatformHeader].ToString().Trim().ToLowerInvariant();
        if (platform is not ("ios" or "android") || token.Length > MaxTokenLength) return false;
        try { return await verifier.VerifyAsync(platform, token, action); }
        catch (Exception e)
        {
            log.LogWarning("Attestation verifier failed. action={Action} reason={Reason}", action, e.GetType().Name);
            return false;
        }
    }

    private static IResult Deny(string code, string message) => Results.Fail(StatusCodes.Status403Forbidden, code, message);
}
