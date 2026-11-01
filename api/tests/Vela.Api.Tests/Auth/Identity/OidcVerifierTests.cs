using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Vela.Api.Auth.Identity;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Auth.Identity;

public class OidcVerifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private const string AppleIssuer = "https://appleid.apple.com";
    private const string AppleAud = "app.vela.test";
    private const string GoogleAud = "google-ios.apps.googleusercontent.com";
    private const string Nonce = "raw-nonce-123";
    private readonly TestJwks _jwks = new();
    private readonly MutableTimeProvider _clock = new(Now);

    private static IOptions<IdentityOptions> Opts() => Options.Create(new IdentityOptions
    {
        Apple = new() { ClientId = AppleAud },
        Google = new() { ClientIds = [GoogleAud, "google-web.apps.googleusercontent.com"] },
    });

    private AppleIdentityVerifier Apple() => new(_jwks, Opts(), _clock);
    private GoogleIdentityVerifier Google() => new(_jwks, Opts(), _clock);

    private string AppleToken(Dictionary<string, object>? claims = null, string aud = AppleAud, string iss = AppleIssuer,
        TimeSpan? life = null, DateTimeOffset? at = null, bool other = false, string alg = SecurityAlgorithms.RsaSha256, string kid = TestJwks.Kid) =>
        _jwks.Mint(iss, aud, at ?? Now, claims ?? new() { ["sub"] = "apple-sub-1", ["email"] = "a@x.test", ["email_verified"] = "true", ["nonce"] = TestJwks.Sha256Hex(Nonce) },
            life, other, alg, kid);

    private string GoogleToken(Dictionary<string, object>? claims = null, string aud = GoogleAud, string iss = "https://accounts.google.com",
        TimeSpan? life = null, DateTimeOffset? at = null) =>
        _jwks.Mint(iss, aud, at ?? Now, claims ?? new() { ["sub"] = "g-sub-1", ["email"] = "g@x.test", ["email_verified"] = true, ["nonce"] = Nonce }, life);

    [Fact]
    public async Task Apple_verifier_accepts_token_signed_by_apple_jwks_with_correct_audience_and_nonce()
    {
        var id = await Apple().VerifyAsync(AppleToken(), Nonce);
        Assert.Equal(new VerifiedIdentity("apple", "apple-sub-1", "a@x.test", true), id);
    }

    [Fact]
    public async Task Apple_email_verified_accepts_boolean_and_string_forms()
    {
        string N() => TestJwks.Sha256Hex(Nonce);
        var b = await Apple().VerifyAsync(AppleToken(new() { ["sub"] = "s", ["email"] = "e@x.test", ["email_verified"] = true, ["nonce"] = N() }), Nonce);
        var f = await Apple().VerifyAsync(AppleToken(new() { ["sub"] = "s", ["email"] = "e@x.test", ["email_verified"] = "false", ["nonce"] = N() }), Nonce);
        var missing = await Apple().VerifyAsync(AppleToken(new() { ["sub"] = "s", ["nonce"] = N() }), Nonce);
        Assert.True(b.EmailVerified);
        Assert.False(f.EmailVerified);
        Assert.False(missing.EmailVerified);
        Assert.Null(missing.Email);
        Assert.False(missing.EmailVerified);
    }

    public static TheoryData<string> BadAppleCases => new() { "aud", "iss", "expired", "nonce", "nonce-raw", "no-nonce", "bad-sig", "alg", "kid", "no-sub", "garbage", "nbf-future" };

    [Theory]
    [MemberData(nameof(BadAppleCases))]
    public async Task Apple_verifier_rejects_wrong_audience_expired_and_bad_nonce(string kind)
    {
        string? nonceArg = Nonce;
        var good = new Dictionary<string, object> { ["sub"] = "s", ["nonce"] = TestJwks.Sha256Hex(Nonce) };
        string token = kind switch
        {
            "aud" => AppleToken(aud: "other.app"),
            "iss" => AppleToken(iss: "https://evil.example"),
            "expired" => AppleToken(at: Now.AddHours(-2), life: TimeSpan.FromHours(1)),
            "nbf-future" => AppleToken(at: Now.AddMinutes(10)),
            "nonce" => AppleToken(new() { ["sub"] = "s", ["nonce"] = TestJwks.Sha256Hex("different") }),
            "nonce-raw" => AppleToken(new() { ["sub"] = "s", ["nonce"] = Nonce }), // must be hashed for Apple
            "no-nonce" => AppleToken(new() { ["sub"] = "s" }),
            "bad-sig" => AppleToken(good, other: true),
            "alg" => AppleToken(good, alg: SecurityAlgorithms.RsaSha512),
            "kid" => AppleToken(good, kid: "unknown"),
            "no-sub" => AppleToken(new() { ["nonce"] = TestJwks.Sha256Hex(Nonce) }),
            _ => "not.a.jwt",
        };
        await Assert.ThrowsAsync<IdentityVerificationException>(() => Apple().VerifyAsync(token, nonceArg));
    }

    [Fact]
    public async Task Apple_verifier_rejects_missing_request_nonce()
    {
        await Assert.ThrowsAsync<IdentityVerificationException>(() => Apple().VerifyAsync(AppleToken(), null));
        await Assert.ThrowsAsync<IdentityVerificationException>(() => Apple().VerifyAsync(AppleToken(), ""));
    }

    [Fact]
    public async Task Verifier_allows_at_most_sixty_seconds_of_clock_skew()
    {
        var token = AppleToken(at: Now.AddMinutes(-10), life: TimeSpan.FromMinutes(10)); // expired exactly now
        _clock.Advance(TimeSpan.FromSeconds(59));
        await Apple().VerifyAsync(token, Nonce);
        _clock.Advance(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<IdentityVerificationException>(() => Apple().VerifyAsync(token, Nonce));
    }

    [Fact]
    public async Task Google_verifier_accepts_valid_token_for_any_configured_client_id_and_both_issuer_forms()
    {
        var a = await Google().VerifyAsync(GoogleToken(), Nonce);
        Assert.Equal(new VerifiedIdentity("google", "g-sub-1", "g@x.test", true), a);
        await Google().VerifyAsync(GoogleToken(aud: "google-web.apps.googleusercontent.com", iss: "accounts.google.com"), Nonce);
    }

    public static TheoryData<string> BadGoogleCases => new() { "aud", "iss", "expired", "nonce", "no-nonce", "hashed-nonce" };

    [Theory]
    [MemberData(nameof(BadGoogleCases))]
    public async Task Google_verifier_rejects_wrong_audience_expired_and_bad_nonce(string kind)
    {
        string token = kind switch
        {
            "aud" => GoogleToken(aud: "unknown-client"),
            "iss" => GoogleToken(iss: "https://evil.example"),
            "expired" => GoogleToken(at: Now.AddHours(-2)),
            "nonce" => GoogleToken(new() { ["sub"] = "s", ["nonce"] = "other" }),
            "hashed-nonce" => GoogleToken(new() { ["sub"] = "s", ["nonce"] = TestJwks.Sha256Hex(Nonce) }),
            _ => GoogleToken(new() { ["sub"] = "s" }),
        };
        await Assert.ThrowsAsync<IdentityVerificationException>(() => Google().VerifyAsync(token, Nonce));
    }

    [Fact]
    public async Task Google_verifier_rejects_missing_request_nonce()
    {
        await Assert.ThrowsAsync<IdentityVerificationException>(() => Google().VerifyAsync(GoogleToken(), null));
    }

    public static TheoryData<string?[]> BlankApple => new() { new string?[] { null }, new[] { "" }, new[] { "   " } };
    public static TheoryData<string[]> BlankGoogle => new() { Array.Empty<string>(), new[] { "" }, new[] { " ", "\t" } };

    [Theory]
    [MemberData(nameof(BlankApple))]
    public async Task Apple_verifier_fails_closed_when_client_id_is_blank(string?[] clientId)
    {
        var opts = Options.Create(new IdentityOptions { Apple = new() { ClientId = clientId[0]! } });
        var v = new AppleIdentityVerifier(_jwks, opts, _clock);
        // Valid in every respect except that no audience is configured.
        var ex = await Assert.ThrowsAsync<IdentityVerificationException>(() => v.VerifyAsync(AppleToken(), Nonce));
        Assert.Equal("The identity token is invalid.", ex.Message);
        foreach (var aud in new[] { "", AppleAud })
            await Assert.ThrowsAsync<IdentityVerificationException>(() => v.VerifyAsync(AppleToken(aud: aud), Nonce));
    }

    [Theory]
    [MemberData(nameof(BlankGoogle))]
    public async Task Google_verifier_fails_closed_when_client_ids_are_empty_or_blank(string[] clientIds)
    {
        var opts = Options.Create(new IdentityOptions { Google = new() { ClientIds = clientIds } });
        var v = new GoogleIdentityVerifier(_jwks, opts, _clock);
        var ex = await Assert.ThrowsAsync<IdentityVerificationException>(() => v.VerifyAsync(GoogleToken(), Nonce));
        Assert.Equal("The identity token is invalid.", ex.Message);
        await Assert.ThrowsAsync<IdentityVerificationException>(() => v.VerifyAsync(GoogleToken(aud: GoogleAud), Nonce));
    }

    [Fact]
    public async Task Fake_verifier_parses_subject_and_optional_email()
    {
        var f = new FakeIdentityVerifier();
        Assert.Equal(new VerifiedIdentity("fake", "bob", "b@x.test", true), await f.VerifyAsync("fake:bob:b@x.test", null));
        Assert.Equal(new VerifiedIdentity("fake", "bob", null, false), await f.VerifyAsync("fake:bob", null));
        await Assert.ThrowsAsync<IdentityVerificationException>(() => f.VerifyAsync("nope", null));
        await Assert.ThrowsAsync<IdentityVerificationException>(() => f.VerifyAsync("fake:", null));
    }
}
