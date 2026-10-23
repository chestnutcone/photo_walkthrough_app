using Microsoft.EntityFrameworkCore;
using Vela.Api.Abuse;
using Vela.Api.Common;
using Vela.Api.Data;

namespace Vela.Api.Auth.Identity;

public record SsoSignInRequest(string? Provider, string? IdToken, string? Nonce);

public static class SsoEndpoint
{
    public static IEndpointRouteBuilder MapSsoEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/auth/sso", HandleAsync).RequireAttestation("sso").WithName("ssoSignIn");
        return app;
    }

    private static async Task<IResult> HandleAsync(SsoSignInRequest? req, IEnumerable<IIdentityVerifier> verifiers,
        IAccountResolver resolver, ITokenService tokens, VelaDbContext db, TimeProvider clock,
        HttpContext ctx, IClientIpResolver ips)
    {
        var verifier = verifiers.FirstOrDefault(v => v.Provider == req?.Provider);
        if (verifier is null)
            return Results.Fail(StatusCodes.Status400BadRequest, "unsupported_provider", "The sign-in provider is not supported.");
        if (string.IsNullOrWhiteSpace(req!.IdToken))
            return Results.Fail(StatusCodes.Status400BadRequest, "validation_failed", "idToken is required.");

        try
        {
            var identity = await verifier.VerifyAsync(req.IdToken, req.Nonce);
            var (userId, isNew) = await resolver.SignInAsync(identity, ips.Resolve(ctx));
            var pair = await tokens.IssueAsync(userId);
            var onboarded = !isNew && await db.Users.AnyAsync(u => u.Id == userId && u.TermsAcceptedAt != null && u.AgeConfirmedAt != null);
            var expiresIn = (int)Math.Round((pair.AccessExpiresAt - clock.GetUtcNow()).TotalSeconds);
            return Results.Ok(new TokenPairResponse(pair.AccessToken, pair.RefreshToken, expiresIn, !onboarded));
        }
        catch (IdentityVerificationException)
        {
            return Results.Fail(StatusCodes.Status401Unauthorized, "invalid_identity_token", "The identity token is invalid.");
        }
        catch (RateLimitedException e)
        {
            ctx.Response.Headers.RetryAfter = ((int)Math.Ceiling(e.RetryAfter.TotalSeconds)).ToString();
            return Results.Fail(StatusCodes.Status429TooManyRequests, "rate_limited", "Too many new accounts from this network. Try again later.");
        }
        catch (AccountSuspendedException)
        {
            return Results.Fail(StatusCodes.Status403Forbidden, "account_suspended", "This account is suspended.");
        }
        catch (AccountDeletedException)
        {
            return Results.Fail(StatusCodes.Status403Forbidden, "account_deleted", "This account has been deleted.");
        }
    }
}
