using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Auth.Identity;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Auth.Identity;

[Collection(PostgresCollection.Name)]
public class SsoEndpointTests(PostgresFixture pg) : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly TestJwks _jwks = new();
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public void Dispose() => _factories.ForEach(f => f.Dispose());

    private WebApplicationFactory<Program> Factory(string env = "Test", bool configureAudiences = true)
    {
        var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(env);
            b.UseSetting("Abuse:RequireAttestation", "false"); // attestation start-up rules are covered in AttestationEndpointTests
            b.UseSetting("ConnectionStrings:Default", pg.ConnectionString);
            b.UseSetting("Admin:Enabled", "false"); // these tests boot production-like environments without admin config
            b.UseSetting("Auth:Jwt:SigningKey", "integration-test-signing-key-0123456789-abc");
            b.UseSetting("Auth:EmailCode:Pepper", "integration-test-pepper-0123456789-abcdef");
            b.UseSetting("Email:Acs:ConnectionString", "endpoint=https://x.communication.azure.com/;accesskey=abc");
            b.UseSetting("Email:SenderAddress", "noreply@example.test");
            b.UseSetting("Storage:ConnectionString", "UseDevelopmentStorage=true");
            b.UseSetting("Storage:PublicBaseUrl", "https://cdn.example.test/");
            b.UseSetting("Web:ViewerUrl", "https://viewer.example.test");
            b.UseSetting("ContentSafety:Endpoint", "https://safety.example.test/");
            b.UseSetting("ContentSafety:Key", "test-key");
            if (configureAudiences)
            {
                b.UseSetting("Auth:Apple:ClientId", "app.vela.test");
                b.UseSetting("Auth:Google:ClientIds:0", "google-ios.apps.googleusercontent.com");
            }
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IJwksProvider>(_jwks);
                s.AddSingleton<TimeProvider>(new MutableTimeProvider(Now));
            });
        });
        _factories.Add(f);
        return f;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> Post(HttpClient c, object req)
    {
        var r = await c.PostAsJsonAsync("/v1/auth/sso", req);
        return (r.StatusCode, JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone());
    }

    [Fact]
    public async Task Sso_sign_in_returns_token_pair()
    {
        var c = Factory().CreateClient();
        var sub = "e2e-" + Guid.NewGuid();

        var (status, body) = await Post(c, new { provider = "fake", idToken = $"fake:{sub}:e@x.test", nonce = "n" });

        Assert.Equal(HttpStatusCode.OK, status);
        var d = body.GetProperty("data");
        Assert.False(string.IsNullOrEmpty(d.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrEmpty(d.GetProperty("refreshToken").GetString()));
        Assert.Equal(900, d.GetProperty("expiresIn").GetInt32());
        Assert.True(d.GetProperty("isNewUser").GetBoolean());

        var (_, again) = await Post(c, new { provider = "fake", idToken = $"fake:{sub}", nonce = "n" });
        Assert.True(again.GetProperty("data").GetProperty("isNewUser").GetBoolean()); // onboarding not complete yet
    }

    [Fact]
    public async Task Sso_sign_in_with_apple_token_works_end_to_end()
    {
        var c = Factory().CreateClient();
        var token = _jwks.Mint("https://appleid.apple.com", "app.vela.test", Now,
            new() { ["sub"] = "ap-" + Guid.NewGuid(), ["nonce"] = TestJwks.Sha256Hex("n1") });

        var (status, _) = await Post(c, new { provider = "apple", idToken = token, nonce = "n1" });
        var (bad, body) = await Post(c, new { provider = "apple", idToken = token, nonce = "wrong" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(HttpStatusCode.Unauthorized, bad);
        Assert.Equal("invalid_identity_token", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("apple")]
    [InlineData("google")]
    public async Task Sso_with_no_audience_configured_returns_401_invalid_identity_token(string provider)
    {
        var c = Factory(configureAudiences: false).CreateClient();
        var nonce = provider == "apple" ? TestJwks.Sha256Hex("n1") : "n1";
        var aud = provider == "apple" ? "app.vela.test" : "google-ios.apps.googleusercontent.com";
        var iss = provider == "apple" ? "https://appleid.apple.com" : "https://accounts.google.com";
        var token = _jwks.Mint(iss, aud, Now, new() { ["sub"] = "x-" + Guid.NewGuid(), ["nonce"] = nonce });

        var (status, body) = await Post(c, new { provider, idToken = token, nonce = "n1" });

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("invalid_identity_token", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("fake:")]
    [InlineData("garbage")]
    public async Task Verification_failure_returns_401_without_leaking_reason(string token)
    {
        var (status, body) = await Post(Factory().CreateClient(), new { provider = "fake", idToken = token, nonce = "n" });
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        var err = body.GetProperty("error");
        Assert.Equal("invalid_identity_token", err.GetProperty("code").GetString());
        Assert.DoesNotContain("fake", err.GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("facebook")]
    [InlineData("")]
    public async Task Unknown_provider_returns_400_unsupported_provider(string provider)
    {
        var (status, body) = await Post(Factory().CreateClient(), new { provider, idToken = "x", nonce = "n" });
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("unsupported_provider", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Missing_id_token_returns_400_validation_failed()
    {
        var (status, body) = await Post(Factory().CreateClient(), new { provider = "apple", nonce = "n" });
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("validation_failed", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Suspended_and_deleted_accounts_get_403()
    {
        var c = Factory().CreateClient();
        var sub = "sd-" + Guid.NewGuid();
        await Post(c, new { provider = "fake", idToken = $"fake:{sub}", nonce = "n" });
        await using var db = pg.NewContext();
        var uid = (await db.Identities.SingleAsync(i => i.Subject == sub)).UserId;

        await db.Users.Where(u => u.Id == uid).ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, Now));
        var (s1, b1) = await Post(c, new { provider = "fake", idToken = $"fake:{sub}", nonce = "n" });
        Assert.Equal(HttpStatusCode.Forbidden, s1);
        Assert.Equal("account_suspended", b1.GetProperty("error").GetProperty("code").GetString());

        await db.Users.Where(u => u.Id == uid).ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, (DateTimeOffset?)null).SetProperty(u => u.DeletedAt, Now));
        var (s2, b2) = await Post(c, new { provider = "fake", idToken = $"fake:{sub}", nonce = "n" });
        Assert.Equal(HttpStatusCode.Forbidden, s2);
        Assert.Equal("account_deleted", b2.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Development", true)]
    [InlineData("Test", true)]
    public void Fake_provider_is_registered_only_in_development_and_test(string env, bool expected)
    {
        var verifiers = Factory(env).Services.GetServices<IIdentityVerifier>().Select(v => v.Provider).ToList();
        Assert.Equal(expected, verifiers.Contains("fake"));
        Assert.Contains("apple", verifiers);
        Assert.Contains("google", verifiers);
    }

    [Fact]
    public async Task Fake_provider_is_not_registered_in_production_environment()
    {
        var (status, body) = await Post(Factory("Production").CreateClient(), new { provider = "fake", idToken = "fake:x", nonce = "n" });
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("unsupported_provider", body.GetProperty("error").GetProperty("code").GetString());
    }
}
