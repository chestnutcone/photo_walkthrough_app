using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Vela.Api.Auth.Identity;
using Vela.Api.Tests.Auth.Identity;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Security;

/// <summary>Classic JWT algorithm attacks against the Apple and Google identity-token verifiers.</summary>
public class OidcAlgorithmConfusionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private const string Nonce = "raw-nonce-123";
    private const string Aud = "app.vela.test";
    private readonly TestJwks _jwks = new();
    private readonly MutableTimeProvider _clock = new(Now);

    private AppleIdentityVerifier Verifier() => new(_jwks, Options.Create(new IdentityOptions { Apple = new() { ClientId = Aud } }), _clock);

    private static string B64(string s) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(s));

    private static string Payload() =>
        $"{{\"iss\":\"https://appleid.apple.com\",\"aud\":\"{Aud}\",\"sub\":\"victim\",\"nonce\":\"{TestJwks.Sha256Hex(Nonce)}\",\"exp\":{Now.AddHours(1).ToUnixTimeSeconds()},\"iat\":{Now.ToUnixTimeSeconds()}}}";

    [Fact]
    public async Task An_unsigned_token_with_alg_none_is_rejected()
    {
        foreach (var alg in new[] { "none", "None", "NONE", "nOnE" })
        {
            var token = $"{B64($"{{\"alg\":\"{alg}\",\"kid\":\"{TestJwks.Kid}\",\"typ\":\"JWT\"}}")}.{B64(Payload())}.";
            await Assert.ThrowsAsync<IdentityVerificationException>(() => Verifier().VerifyAsync(token, Nonce));
        }
    }

    [Fact]
    public async Task A_token_signed_with_hs256_using_the_providers_public_key_as_the_secret_is_rejected()
    {
        var modulus = ((RsaSecurityKey)_jwks.PublicKey).Parameters.Modulus!;
        var secret = new SymmetricSecurityKey(modulus) { KeyId = TestJwks.Kid };
        var handler = new JsonWebTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://appleid.apple.com", Audience = Aud, Expires = Now.AddHours(1).UtcDateTime, NotBefore = Now.UtcDateTime,
            Subject = new ClaimsIdentity([new Claim("sub", "victim"), new Claim("nonce", TestJwks.Sha256Hex(Nonce))]),
            SigningCredentials = new SigningCredentials(secret, SecurityAlgorithms.HmacSha256), // the kid header comes from the key
        });

        // Control 1: the forgery is real. It names HS256 and the provider's kid, and a validator that trusted the
        // header's algorithm would accept it with the public modulus as the secret.
        var parsed = handler.ReadJsonWebToken(token);
        Assert.Equal(SecurityAlgorithms.HmacSha256, parsed.Alg);
        Assert.Equal(TestJwks.Kid, parsed.Kid);
        var naive = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "https://appleid.apple.com", ValidAudience = Aud, IssuerSigningKey = secret,
            LifetimeValidator = (_, _, _, _) => true,
        });
        Assert.True(naive.IsValid, "the forged token must be well-formed, otherwise this test proves nothing");

        // The verifier refuses it, and for the right reason: on the algorithm, before it ever looks up a key.
        await Assert.ThrowsAsync<IdentityVerificationException>(() => Verifier().VerifyAsync(token, Nonce));
        Assert.Equal(0, _jwks.Fetches);
    }

    [Fact]
    public async Task A_token_without_a_kid_or_with_an_attacker_chosen_key_url_is_rejected()
    {
        var noKid = _jwks.Mint("https://appleid.apple.com", Aud, Now, new() { ["sub"] = "victim", ["nonce"] = TestJwks.Sha256Hex(Nonce) }, kid: "");
        await Assert.ThrowsAsync<IdentityVerificationException>(() => Verifier().VerifyAsync(noKid, Nonce));

        // jku/x5u headers must never make the verifier fetch a key from the token's own say-so: it only ever asks the provider's JWKS.
        var forged = $"{B64($"{{\"alg\":\"RS256\",\"kid\":\"{TestJwks.Kid}\",\"jku\":\"https://evil.example/keys\"}}")}.{B64(Payload())}.{Base64UrlEncoder.Encode(new byte[256])}";
        await Assert.ThrowsAsync<IdentityVerificationException>(() => Verifier().VerifyAsync(forged, Nonce));
        Assert.Equal(1, _jwks.Fetches); // only the fixed provider lookup, for the kid the token named
    }
}
