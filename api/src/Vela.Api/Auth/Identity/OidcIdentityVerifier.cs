using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Vela.Api.Auth.Identity;

/// <summary>Shared OIDC id-token validation: signature (RS256 only), issuer, audience, lifetime, nonce.</summary>
public abstract class OidcIdentityVerifier(IJwksProvider jwks, TimeProvider clock) : IIdentityVerifier
{
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(60);

    public abstract string Provider { get; }
    protected abstract string JwksUri { get; }
    protected abstract string[] Issuers { get; }
    protected abstract string[] Audiences { get; }
    /// <summary>The value the token's nonce claim must carry for this raw request nonce.</summary>
    protected abstract string ExpectedNonceClaim(string rawNonce);

    public async Task<VerifiedIdentity> VerifyAsync(string idToken, string? nonce)
    {
        if (string.IsNullOrEmpty(nonce) || string.IsNullOrWhiteSpace(idToken) || Audiences.Length == 0)
            throw new IdentityVerificationException();
        try
        {
            return await ValidateAsync(idToken, nonce);
        }
        catch (IdentityVerificationException) { throw; }
        catch (Exception e)
        {
            throw new IdentityVerificationException(inner: e);
        }
    }

    private async Task<VerifiedIdentity> ValidateAsync(string idToken, string nonce)
    {
        var handler = new JsonWebTokenHandler();
        var parsed = handler.ReadJsonWebToken(idToken);
        if (parsed.Alg != SecurityAlgorithms.RsaSha256 || string.IsNullOrEmpty(parsed.Kid))
            throw new IdentityVerificationException();
        var key = await jwks.GetKeyAsync(JwksUri, parsed.Kid) ?? throw new IdentityVerificationException();

        var result = await handler.ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuers = Issuers,
            ValidAudiences = Audiences,
            IssuerSigningKey = key,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = Skew,
            LifetimeValidator = (notBefore, expires, _, p) =>
            {
                var now = clock.GetUtcNow().UtcDateTime;
                return expires is { } e && e + p.ClockSkew > now && (notBefore is not { } nb || nb - p.ClockSkew <= now);
            },
        });
        if (!result.IsValid) throw new IdentityVerificationException(inner: result.Exception);

        var claims = result.Claims;
        if (!claims.TryGetValue("nonce", out var n) || n is not string tokenNonce
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(tokenNonce), Encoding.UTF8.GetBytes(ExpectedNonceClaim(nonce))))
            throw new IdentityVerificationException();
        if (!claims.TryGetValue("sub", out var s) || s is not string sub || sub.Length == 0)
            throw new IdentityVerificationException();

        var email = claims.TryGetValue("email", out var e2) ? e2 as string : null;
        var verified = email is not null && claims.TryGetValue("email_verified", out var v) && v switch
        {
            bool b => b,
            string str => string.Equals(str, "true", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
        return new VerifiedIdentity(Provider, sub, email, verified);
    }
}

public sealed class AppleIdentityVerifier(IJwksProvider jwks, IOptions<IdentityOptions> options, TimeProvider clock)
    : OidcIdentityVerifier(jwks, clock)
{
    public override string Provider => "apple";
    protected override string JwksUri => "https://appleid.apple.com/auth/keys";
    protected override string[] Issuers => ["https://appleid.apple.com"];
    protected override string[] Audiences =>
        string.IsNullOrWhiteSpace(options.Value.Apple.ClientId) ? [] : [options.Value.Apple.ClientId];
    protected override string ExpectedNonceClaim(string rawNonce) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawNonce))).ToLowerInvariant();
}

public sealed class GoogleIdentityVerifier(IJwksProvider jwks, IOptions<IdentityOptions> options, TimeProvider clock)
    : OidcIdentityVerifier(jwks, clock)
{
    public override string Provider => "google";
    protected override string JwksUri => "https://www.googleapis.com/oauth2/v3/certs";
    protected override string[] Issuers => ["https://accounts.google.com", "accounts.google.com"];
    protected override string[] Audiences => options.Value.Google.ClientIds.Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();
    protected override string ExpectedNonceClaim(string rawNonce) => rawNonce;
}
