using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Abuse;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Abuse.AbuseKit;

namespace Vela.Api.Tests.Abuse;

[Collection(PostgresCollection.Name)]
public class AttestationEndpointTests(PostgresFixture pg) : IDisposable
{
    private readonly List<ProfileTestHost> _hosts = [];
    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private async Task<ProfileTestHost> NewHost(bool require, string? cs = null)
    {
        var flag = require ? "true" : "false";
        var h = await Host(pg, new() { ["Abuse:RequireAttestation:Android"] = flag, ["Abuse:RequireAttestation:Ios"] = flag }, cs: cs);
        _hosts.Add(h);
        return h;
    }

    private static Action<HttpRequestMessage> Attest(string? token, string? platform = "android") => m =>
    {
        if (token is not null) m.Headers.Add("X-Attestation", token);
        if (platform is not null) m.Headers.Add("X-Platform", platform);
    };

    [Fact]
    public async Task Missing_attestation_is_rejected_when_required_and_allowed_when_not()
    {
        var required = (await NewHost(true)).Anonymous();
        var optional = (await NewHost(false)).Anonymous();

        var denied = await EmailRequest(required, NewEmail());
        var allowed = await EmailRequest(optional, NewEmail());

        Assert.Equal(HttpStatusCode.Forbidden, denied.Status);
        Assert.Equal("attestation_required", denied.ErrorCode);
        Assert.Equal(HttpStatusCode.OK, allowed.Status);
    }

    [Fact]
    public async Task Valid_attestation_passes_when_required()
    {
        var c = (await NewHost(true)).Anonymous();
        Assert.Equal(HttpStatusCode.OK, (await EmailRequest(c, NewEmail(), tweak: Attest("valid"))).Status);
    }

    [Theory]
    [InlineData("forged", "android")]
    [InlineData("valid", "windows")]
    [InlineData("valid", null)]
    public async Task Invalid_attestation_token_is_rejected(string token, string? platform)
    {
        var c = (await NewHost(true)).Anonymous();

        var r = await EmailRequest(c, NewEmail(), tweak: Attest(token, platform));

        Assert.Equal(HttpStatusCode.Forbidden, r.Status);
        Assert.Equal("attestation_failed", r.ErrorCode);
    }

    [Fact]
    public async Task Invalid_token_is_not_checked_when_attestation_is_not_required()
    {
        var c = (await NewHost(false)).Anonymous();
        Assert.Equal(HttpStatusCode.OK, (await EmailRequest(c, NewEmail(), tweak: Attest("forged"))).Status);
    }

    [Fact]
    public async Task Sso_requires_attestation_when_enabled()
    {
        var c = (await NewHost(true)).Anonymous();
        var body = JsonSerializer.Serialize(new { provider = "fake", idToken = "fake:s-" + Guid.NewGuid(), nonce = "n" });

        var denied = await Send(c, HttpMethod.Post, "/v1/auth/sso", body, NewIp());
        var ok = await Send(c, HttpMethod.Post, "/v1/auth/sso", body, NewIp(), Attest("valid", "ios"));

        Assert.Equal("attestation_required", denied.ErrorCode);
        Assert.Equal(HttpStatusCode.OK, ok.Status);
    }

    [Fact]
    public async Task Create_draft_requires_attestation_when_enabled_and_each_action_is_named()
    {
        var cs = await pg.CreateIsolatedDatabaseAsync();
        var open = await NewHost(false, cs);
        var user = await open.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var strict = await NewHost(true, cs);
        var token = user.Client.DefaultRequestHeaders.Authorization;
        var client = strict.Anonymous();
        client.DefaultRequestHeaders.Authorization = token;

        var denied = await Send(client, HttpMethod.Post, "/v1/works", "{\"title\":\"x\",\"contentType\":\"image/jpeg\"}", NewIp());
        var ok = await Send(client, HttpMethod.Post, "/v1/works", "{\"title\":\"x\",\"contentType\":\"image/jpeg\"}", NewIp(), Attest("valid"));

        Assert.Equal("attestation_required", denied.ErrorCode);
        Assert.Equal(HttpStatusCode.OK, ok.Status);
        Assert.Equal(["create_work"], strict.Services.GetRequiredService<FakeAttestationVerifier>().Actions.Where(a => a == "create_work").ToArray());
    }

    [Fact]
    public async Task Verifier_exceptions_fail_closed()
    {
        var h = new ProfileTestHost(pg, new Dictionary<string, string> { ["Abuse:RequireAttestation:Android"] = "true" }, await pg.CreateIsolatedDatabaseAsync(),
            s => s.AddSingleton<IAttestationVerifier, ThrowingVerifier>());
        _hosts.Add(h);

        var r = await EmailRequest(h.Anonymous(), NewEmail(), tweak: Attest("valid"));

        Assert.Equal(HttpStatusCode.Forbidden, r.Status);
        Assert.Equal("attestation_failed", r.ErrorCode);
    }

    private sealed class ThrowingVerifier : IAttestationVerifier
    {
        public Task<bool> VerifyAsync(string platform, string token, string action) => throw new InvalidOperationException("boom");
    }

    private static Dictionary<string, string> ProductionSettings(Dictionary<string, string>? extra = null)
    {
        var d = new Dictionary<string, string>
        {
            ["Admin:Enabled"] = "false",
            ["Admin:BanPepper"] = new string('p', 40),
            ["Email:Acs:ConnectionString"] = "endpoint=https://x.communication.azure.com/;accesskey=abc",
            ["Email:SenderAddress"] = "noreply@example.test",
            ["Storage:ConnectionString"] = "UseDevelopmentStorage=true",
            ["Storage:PublicBaseUrl"] = "https://cdn.example.test/",
            ["Web:ViewerUrl"] = "https://viewer.example.test",
            ["ContentSafety:Endpoint"] = "https://safety.example.test/",
            ["ContentSafety:Key"] = "test-key",
        };
        foreach (var (k, v) in extra ?? []) d[k] = v;
        return d;
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Production(Dictionary<string, string> settings)
    {
        var f = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Launch:RequireLaunchSettings", "false"); // launch blockers are covered in LaunchSettingsTests
            b.UseSetting("ConnectionStrings:Default", pg.ConnectionString);
            b.UseSetting("Auth:Jwt:SigningKey", "integration-test-signing-key-0123456789-abc");
            b.UseSetting("Auth:EmailCode:Pepper", "integration-test-pepper-0123456789-abcdef");
            foreach (var (k, v) in settings) b.UseSetting(k, v);
        });
        return f;
    }

    private const string PlayPackage = "com.example.app";
    private static readonly Dictionary<string, string> PlayKeys = new()
    {
        ["Abuse:PlayIntegrity:PackageName"] = PlayPackage,
        ["Abuse:PlayIntegrity:DecryptionKey"] = Convert.ToBase64String(new byte[32]),
        ["Abuse:PlayIntegrity:VerificationKey"] = Convert.ToBase64String(System.Security.Cryptography.ECDsa.Create(
            System.Security.Cryptography.ECCurve.NamedCurves.nistP256).ExportSubjectPublicKeyInfo()),
    };

    [Fact]
    public void Production_requires_play_integrity_by_default_and_names_every_missing_key_but_not_app_attest()
    {
        // Owner decision 2026-11-03: Android only at launch; App Attest is deferred.
        using var f = Production(ProductionSettings());
        var ex = Assert.Throws<InvalidOperationException>(() => f.CreateClient());
        foreach (var key in new[] { "Abuse:PlayIntegrity:PackageName", "Abuse:PlayIntegrity:DecryptionKey", "Abuse:PlayIntegrity:VerificationKey" })
            Assert.Contains(key, ex.Message);
        Assert.Contains("Abuse:RequireAttestation:Android", ex.Message);
        Assert.DoesNotContain("Abuse:AppAttest", ex.Message);
    }

    [Fact]
    public void Production_starts_with_play_integrity_only_and_requires_android_not_ios()
    {
        using var f = Production(ProductionSettings(PlayKeys));
        f.CreateClient();
        var o = f.Services.GetRequiredService<AbuseOptions>();
        Assert.True(o.RequireAttestationAndroid);
        Assert.False(o.RequireAttestationIos);
    }

    [Fact]
    public void Production_refuses_to_start_when_ios_is_required_without_app_attest()
    {
        using var f = Production(ProductionSettings(new(PlayKeys) { ["Abuse:RequireAttestation:Ios"] = "true" }));
        var ex = Assert.Throws<InvalidOperationException>(() => f.CreateClient());
        Assert.Contains("Abuse:AppAttest:AppId", ex.Message);
        Assert.Contains("Abuse:RequireAttestation:Ios", ex.Message);
    }

    [Theory]
    [InlineData("Production", "true")]
    [InlineData("Production", "false")]
    [InlineData("Test", "false")]
    public void The_old_single_switch_is_refused_so_it_cannot_silently_mean_something_else(string env, string value)
    {
        // AbuseOptions.Load directly: a host built with this key could leak it into hosts built in parallel.
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(PlayKeys.Select(k => KeyValuePair.Create(k.Key, (string?)k.Value)))
            {
                ["Abuse:RequireAttestation"] = value,
            }).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => AbuseOptions.Load(config, new FakeEnv(env)));

        Assert.Contains("Abuse:RequireAttestation:Android", ex.Message);
    }

    private sealed class FakeEnv(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Vela.Api";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    [Fact]
    public async Task Production_starts_without_attestation_config_when_not_required_and_uses_a_rejecting_verifier()
    {
        using var f = Production(ProductionSettings(new() { ["Abuse:RequireAttestation:Android"] = "false" }));
        f.CreateClient();
        var verifier = f.Services.GetRequiredService<IAttestationVerifier>();
        Assert.IsNotType<FakeAttestationVerifier>(verifier);
        Assert.False(await verifier.VerifyAsync("android", "valid", "x"));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void Development_and_test_use_the_fake_verifier(string env)
    {
        using var f = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(env);
            b.UseSetting("Launch:RequireLaunchSettings", "false"); // launch blockers are covered in LaunchSettingsTests
            b.UseSetting("ConnectionStrings:Default", pg.ConnectionString);
            b.UseSetting("Auth:Jwt:SigningKey", "integration-test-signing-key-0123456789-abc");
            b.UseSetting("Auth:EmailCode:Pepper", "integration-test-pepper-0123456789-abcdef");
            b.UseSetting("Admin:Enabled", "false");
            b.UseSetting("Admin:BanPepper", new string('p', 40)); // required even when the pages are off: earlier bans are matched with it
        });
        f.CreateClient();
        Assert.IsType<FakeAttestationVerifier>(f.Services.GetRequiredService<IAttestationVerifier>());
    }
}
