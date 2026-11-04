using System.Net;
using System.Text.Json;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Abuse.AbuseKit;

namespace Vela.Api.Tests.Abuse;

/// <summary>
/// Owner decision 2026-11-03: at launch attestation is enforced on Android (Play Integrity) and not on iOS (App Attest deferred).
/// The launch configuration is Abuse:RequireAttestation:Android=true (the default outside Development and Test) and
/// Abuse:RequireAttestation:Ios=false (the default). Only an explicit iOS claim selects the iOS rule; every unattested request
/// that passes is held to the tighter unattested limits.
/// </summary>
[Collection(PostgresCollection.Name)]
public class PlatformAttestationTests(PostgresFixture pg) : IDisposable
{
    private readonly List<ProfileTestHost> _hosts = [];
    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    /// <summary>The production rule set (Android on, iOS off) on the fake verifier, with optional limit overrides.</summary>
    private async Task<ProfileTestHost> LaunchHost(Dictionary<string, string>? extra = null, string? cs = null)
    {
        var settings = new Dictionary<string, string> { ["Abuse:RequireAttestation:Android"] = "true" };
        foreach (var (k, v) in extra ?? []) settings[k] = v;
        var h = await Host(pg, settings, cs: cs);
        _hosts.Add(h);
        return h;
    }

    private static Action<HttpRequestMessage> Headers(string? token, params string[] platforms) => m =>
    {
        if (token is not null) m.Headers.Add("X-Attestation", token);
        foreach (var p in platforms) m.Headers.Add("X-Platform", p);
    };

    private static string SsoBody() => JsonSerializer.Serialize(new { provider = "fake", idToken = "fake:p-" + Guid.NewGuid(), nonce = "n" });

    [Fact]
    public async Task Android_without_a_token_is_refused()
    {
        var c = (await LaunchHost()).Anonymous();

        var email = await EmailRequest(c, NewEmail(), tweak: Headers(null, "android"));
        var sso = await Send(c, HttpMethod.Post, "/v1/auth/sso", SsoBody(), NewIp(), Headers(null, "android"));

        Assert.Equal((HttpStatusCode.Forbidden, "attestation_required"), (email.Status, email.ErrorCode));
        Assert.Equal((HttpStatusCode.Forbidden, "attestation_required"), (sso.Status, sso.ErrorCode));
    }

    [Fact]
    public async Task Android_with_a_valid_token_is_accepted()
    {
        var c = (await LaunchHost()).Anonymous();

        Assert.Equal(HttpStatusCode.OK, (await EmailRequest(c, NewEmail(), tweak: Headers("valid", "android"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(c, HttpMethod.Post, "/v1/auth/sso", SsoBody(), NewIp(), Headers("valid", "android"))).Status);
    }

    [Fact]
    public async Task Android_with_a_forged_token_is_refused()
    {
        var c = (await LaunchHost()).Anonymous();
        var r = await EmailRequest(c, NewEmail(), tweak: Headers("forged", "android"));
        Assert.Equal((HttpStatusCode.Forbidden, "attestation_failed"), (r.Status, r.ErrorCode));
    }

    [Fact]
    public async Task Ios_is_accepted_without_a_token_in_the_launch_configuration()
    {
        var c = (await LaunchHost()).Anonymous();

        Assert.Equal(HttpStatusCode.OK, (await EmailRequest(c, NewEmail(), tweak: Headers(null, "ios"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(c, HttpMethod.Post, "/v1/auth/sso", SsoBody(), NewIp(), Headers(null, "ios"))).Status);
    }

    [Fact]
    public async Task Create_draft_follows_the_same_platform_rule()
    {
        var cs = await pg.CreateIsolatedDatabaseAsync();
        var open = await Host(pg, cs: cs); // Test default: no enforcement, so the helper can sign in
        _hosts.Add(open);
        var user = await open.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var client = (await LaunchHost(cs: cs)).Anonymous();
        client.DefaultRequestHeaders.Authorization = user.Client.DefaultRequestHeaders.Authorization;
        const string body = "{\"title\":\"x\",\"contentType\":\"image/jpeg\"}";

        var android = await Send(client, HttpMethod.Post, "/v1/works", body, NewIp(), Headers(null, "android"));
        var ios = await Send(client, HttpMethod.Post, "/v1/works", body, NewIp(), Headers(null, "ios"));

        Assert.Equal("attestation_required", android.ErrorCode);
        Assert.Equal(HttpStatusCode.OK, ios.Status);
    }

    [Theory]
    [InlineData(null)]           // no platform header at all
    [InlineData("")]
    [InlineData("web")]
    [InlineData("windows")]
    [InlineData("i0s")]
    [InlineData("ios android")]
    [InlineData("ios|android")]  // two headers: the claim is ambiguous, so not iOS
    [InlineData("android|ios")]
    [InlineData("ios,android")]
    public async Task Anything_but_a_single_ios_claim_gets_the_android_rule(string? headers)
    {
        var c = (await LaunchHost()).Anonymous();
        var platforms = headers?.Split('|') ?? [];

        var r = await EmailRequest(c, NewEmail(), tweak: Headers(null, platforms));

        Assert.Equal((HttpStatusCode.Forbidden, "attestation_required"), (r.Status, r.ErrorCode));
    }

    [Fact]
    public async Task An_ios_claim_is_held_to_the_tighter_unattested_limit_while_attested_android_is_not()
    {
        var c = (await LaunchHost(new() { ["Abuse:RateLimits:unattested-email_request:Permit"] = "2" })).Anonymous();
        var ip = NewIp();

        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.OK, (await EmailRequest(c, NewEmail(), ip, Headers(null, "ios"))).Status);
        var third = await EmailRequest(c, NewEmail(), ip, Headers(null, "ios"));
        var androidSameIp = await EmailRequest(c, NewEmail(), ip, Headers("valid", "android"));

        Assert.Equal((HttpStatusCode.TooManyRequests, "rate_limited"), (third.Status, third.ErrorCode));
        Assert.True(third.Response.Headers.RetryAfter is not null);
        Assert.Equal(HttpStatusCode.OK, androidSameIp.Status);
    }

    [Fact]
    public async Task A_valid_android_token_sent_with_an_ios_claim_counts_as_unattested()
    {
        // The token is ignored when the claimed platform's rule is off, so a claim never mixes rules.
        var c = (await LaunchHost(new() { ["Abuse:RateLimits:unattested-email_request:Permit"] = "1" })).Anonymous();
        var ip = NewIp();

        Assert.Equal(HttpStatusCode.OK, (await EmailRequest(c, NewEmail(), ip, Headers("valid", "ios"))).Status);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await EmailRequest(c, NewEmail(), ip, Headers("valid", "ios"))).Status);
    }

    [Fact]
    public async Task Unattested_limits_are_per_address_and_per_action()
    {
        var c = (await LaunchHost(new()
        {
            ["Abuse:RateLimits:unattested-email_request:Permit"] = "1",
            ["Abuse:RateLimits:unattested-sso:Permit"] = "1",
        })).Anonymous();
        var ip = NewIp();

        Assert.Equal(HttpStatusCode.OK, (await EmailRequest(c, NewEmail(), ip, Headers(null, "ios"))).Status);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await EmailRequest(c, NewEmail(), ip, Headers(null, "ios"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await EmailRequest(c, NewEmail(), NewIp(), Headers(null, "ios"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(c, HttpMethod.Post, "/v1/auth/sso", SsoBody(), ip, Headers(null, "ios"))).Status);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(c, HttpMethod.Post, "/v1/auth/sso", SsoBody(), ip, Headers(null, "ios"))).Status);
    }

    [Fact]
    public async Task Unattested_email_codes_are_also_limited_per_address_and_target_email()
    {
        var c = (await LaunchHost(new() { ["Abuse:RateLimits:unattested-email_target:Permit"] = "2" })).Anonymous();
        var ip = NewIp();
        var victim = NewEmail();

        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.OK, (await EmailRequest(c, victim, ip, Headers(null, "ios"))).Status);
        var third = await EmailRequest(c, victim, ip, Headers(null, "ios"));
        var otherEmailSameIp = await EmailRequest(c, NewEmail(), ip, Headers(null, "ios"));
        var sameEmailOtherIp = await EmailRequest(c, victim, NewIp(), Headers(null, "ios"));

        Assert.Equal((HttpStatusCode.TooManyRequests, "rate_limited"), (third.Status, third.ErrorCode));
        Assert.True(third.Response.Headers.RetryAfter is not null);
        Assert.Equal(HttpStatusCode.OK, otherEmailSameIp.Status);
        Assert.Equal(HttpStatusCode.OK, sameEmailOtherIp.Status);
    }

    [Fact]
    public async Task Junk_email_requests_do_not_spend_the_shared_address_budget()
    {
        var c = (await LaunchHost(new() { ["Abuse:RateLimits:unattested-email_request:Permit"] = "1" })).Anonymous();
        var ip = NewIp();

        foreach (var junk in new[] { "not-an-email", "", "x@mailinator.com" })
            Assert.Equal(HttpStatusCode.BadRequest, (await EmailRequest(c, junk, ip, Headers(null, "ios"))).Status);

        Assert.Equal(HttpStatusCode.OK, (await EmailRequest(c, NewEmail(), ip, Headers(null, "ios"))).Status);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await EmailRequest(c, NewEmail(), ip, Headers(null, "ios"))).Status);
    }

    [Fact]
    public async Task A_pair_refusal_does_not_spend_the_address_budget()
    {
        var c = (await LaunchHost(new()
        {
            ["Abuse:RateLimits:unattested-email_request:Permit"] = "2",
            ["Abuse:RateLimits:unattested-email_target:Permit"] = "1",
        })).Anonymous();
        var ip = NewIp();
        var spammed = NewEmail();

        Assert.Equal(HttpStatusCode.OK, (await EmailRequest(c, spammed, ip, Headers(null, "ios"))).Status);
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.TooManyRequests, (await EmailRequest(c, spammed, ip, Headers(null, "ios"))).Status);

        Assert.Equal(HttpStatusCode.OK, (await EmailRequest(c, NewEmail(), ip, Headers(null, "ios"))).Status);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await EmailRequest(c, NewEmail(), ip, Headers(null, "ios"))).Status);
    }

    [Fact]
    public async Task Only_verified_sso_sign_ins_spend_the_unattested_sso_budget()
    {
        var c = (await LaunchHost(new() { ["Abuse:RateLimits:unattested-sso:Permit"] = "1" })).Anonymous();
        var ip = NewIp();
        string Body(string? provider, string? token) => JsonSerializer.Serialize(new { provider, idToken = token, nonce = "n" });

        Assert.Equal(HttpStatusCode.BadRequest, (await Send(c, HttpMethod.Post, "/v1/auth/sso", Body("nope", "fake:x"), ip, Headers(null, "ios"))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(c, HttpMethod.Post, "/v1/auth/sso", Body("fake", ""), ip, Headers(null, "ios"))).Status);
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(c, HttpMethod.Post, "/v1/auth/sso", Body("fake", "garbage"), ip, Headers(null, "ios"))).Status);

        Assert.Equal(HttpStatusCode.OK, (await Send(c, HttpMethod.Post, "/v1/auth/sso", SsoBody(), ip, Headers(null, "ios"))).Status);
        var limited = await Send(c, HttpMethod.Post, "/v1/auth/sso", SsoBody(), ip, Headers(null, "ios"));
        var junkWhileLimited = await Send(c, HttpMethod.Post, "/v1/auth/sso", Body("fake", "garbage"), ip, Headers(null, "ios"));

        Assert.Equal((HttpStatusCode.TooManyRequests, "rate_limited"), (limited.Status, limited.ErrorCode));
        Assert.True(limited.Response.Headers.RetryAfter is not null);
        Assert.Equal(HttpStatusCode.TooManyRequests, junkWhileLimited.Status); // an exhausted address does not even verify tokens
    }

    [Fact]
    public async Task Unattested_create_work_is_limited_per_account_not_per_address()
    {
        var cs = await pg.CreateIsolatedDatabaseAsync();
        var open = await Host(pg, cs: cs);
        _hosts.Add(open);
        var a = await open.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var b = await open.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var host = await LaunchHost(new() { ["Abuse:RateLimits:unattested-create_work:Permit"] = "1" }, cs);
        const string body = "{\"title\":\"x\",\"contentType\":\"image/jpeg\"}";
        var ip = NewIp();

        async Task<HttpStatusCode> Post(TestUser u)
        {
            var client = host.Anonymous();
            client.DefaultRequestHeaders.Authorization = u.Client.DefaultRequestHeaders.Authorization;
            return (await Send(client, HttpMethod.Post, "/v1/works", body, ip, Headers(null, "ios"))).Status;
        }

        Assert.Equal(HttpStatusCode.OK, await Post(a));
        Assert.Equal(HttpStatusCode.OK, await Post(b));   // same address, other account: own budget
        Assert.Equal(HttpStatusCode.TooManyRequests, await Post(a));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Post(b));
    }

    [Fact]
    public async Task With_ios_enforcement_on_an_ios_request_needs_a_token_too()
    {
        var c = (await LaunchHost(new() { ["Abuse:RequireAttestation:Ios"] = "true" })).Anonymous();

        var denied = await EmailRequest(c, NewEmail(), tweak: Headers(null, "ios"));
        var ok = await EmailRequest(c, NewEmail(), tweak: Headers("valid", "ios"));

        Assert.Equal("attestation_required", denied.ErrorCode);
        Assert.Equal(HttpStatusCode.OK, ok.Status);
    }
}
