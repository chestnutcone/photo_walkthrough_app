using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Tests.Abuse;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Share;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Works.WorkTestKit;

namespace Vela.Api.Tests.Security;

/// <summary>
/// A small population shared by the security tests: two onboarded users with distinct, recognisable login emails, a draft,
/// a published story with a reply, a notification and an export for the first user. Built once per test run.
/// </summary>
public sealed class SecurityWorld : IDisposable
{
    public static readonly Dictionary<string, string> Settings = new(ShareTestKit.Config)
    {
        ["Auth:UserStateCacheSeconds"] = "0",
        ["Abuse:RateLimits:anon-read:Permit"] = "1000000",
        ["Abuse:RateLimits:write:Permit"] = "1000000",
        ["Abuse:RateLimits:upload:Permit"] = "1000000", ["Abuse:RateLimits:user-read:Permit"] = "1000000",
        ["Abuse:RateLimits:auth-email:Permit"] = "1000000",
        ["Abuse:RateLimits:public-form:Permit"] = "1000000",
    };

    public ProfileTestHost Host { get; }
    public TestUser Alice { get; }
    public TestUser Bob { get; }
    public string AliceLoginEmail { get; }
    public string BobLoginEmail { get; }
    public string AliceSubject { get; }
    public string AliceHandle { get; }
    public Guid AliceDraft { get; }
    public Guid AliceWork { get; }
    public Guid AliceReply { get; }
    public Guid AliceNotification { get; }
    public Guid AliceExport { get; }
    public HttpClient Anonymous { get; }

    private SecurityWorld(ProfileTestHost host, TestUser alice, TestUser bob, string aliceEmail, string bobEmail, string aliceSubject,
        string handle, Guid draft, Guid work, Guid reply, Guid notification, Guid export)
    {
        Host = host; Alice = alice; Bob = bob; AliceLoginEmail = aliceEmail; BobLoginEmail = bobEmail; AliceSubject = aliceSubject;
        AliceHandle = handle; AliceDraft = draft; AliceWork = work; AliceReply = reply; AliceNotification = notification; AliceExport = export;
        Anonymous = host.Anonymous();
    }

    private static SecurityWorld? _shared;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>The read-mostly world every matrix test shares. Tests that change accounts build their own with <see cref="CreateAsync"/>.</summary>
    public static async Task<SecurityWorld> SharedAsync(PostgresFixture pg)
    {
        await Gate.WaitAsync();
        try { return _shared ??= await CreateAsync(pg); }
        finally { Gate.Release(); }
    }

    public static async Task<SecurityWorld> CreateAsync(PostgresFixture pg, Dictionary<string, string>? extraSettings = null)
    {
        var settings = new Dictionary<string, string>(Settings);
        foreach (var (k, v) in extraSettings ?? []) settings[k] = v;
        var host = await AbuseKit.Host(pg, settings);
        return await Populate(host);
    }

    public static async Task<SecurityWorld> CreateWithServicesAsync(PostgresFixture pg, Action<IServiceCollection> services, Dictionary<string, string>? extraSettings = null)
    {
        var settings = new Dictionary<string, string>(Settings);
        foreach (var (k, v) in extraSettings ?? []) settings[k] = v;
        var host = new ProfileTestHost(pg, settings, await pg.CreateIsolatedDatabaseAsync(), s =>
        {
            s.AddSingleton<Vela.Api.Common.IClientIpResolver, HeaderIp>();
            services(s);
        });
        return await Populate(host);
    }

    private static async Task<SecurityWorld> Populate(ProfileTestHost host)
    {
        var aliceEmail = $"alice-login-{Guid.NewGuid():N}@secret-login.test";
        var bobEmail = $"bob-login-{Guid.NewGuid():N}@secret-login.test";
        var alice = await host.SignIn(aliceEmail);
        var bob = await host.SignIn(bobEmail);
        var handle = ProfileTestHost.UniqueHandle("al");
        Assert.Equal(HttpStatusCode.OK, (await alice.Onboard(handle, "Alice Artist")).Status);
        Assert.Equal(HttpStatusCode.OK, (await bob.Onboard(ProfileTestHost.UniqueHandle("bo"), "Bob Viewer")).Status);

        var draft = await ReadyDraft(host, alice, "Alice draft");
        var work = await ShareTestKit.Published(host, alice, "Alice published story");
        var reply = await alice.Post($"/v1/works/{work}/chapters/0/replies", new { body = "A first reply from the artist." });
        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal(HttpStatusCode.OK, (await bob.Put($"/v1/artists/{alice.Id}/follow")).Status);
        var notifications = await alice.Get("/v1/notifications");
        var export = await alice.Post("/v1/me/export", new { });
        Assert.Equal(HttpStatusCode.OK, export.Status);

        string subject;
        await using (var db = host.Db()) subject = (await db.Identities.SingleAsync(i => i.UserId == alice.Id)).Subject;

        return new SecurityWorld(host, alice, bob, aliceEmail, bobEmail, subject, handle, draft, work,
            reply.Data.GetProperty("id").GetGuid(), notifications.Data.EnumerateArray().First().GetProperty("id").GetGuid(),
            export.Data.GetProperty("id").GetGuid());
    }

    /// <summary>A fresh onboarded user on this host (own token), for tests that change the account.</summary>
    public async Task<TestUser> NewUserAsync()
    {
        var u = await Host.SignIn($"extra-{Guid.NewGuid():N}@secret-login.test");
        Assert.Equal(HttpStatusCode.OK, (await u.Onboard(ProfileTestHost.UniqueHandle("ex"))).Status);
        return u;
    }

    public HttpClient ClientWithToken(string token)
    {
        var c = Host.Anonymous();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    public static string TokenOf(TestUser u) => u.Client.DefaultRequestHeaders.Authorization!.Parameter!;

    public static async Task<(HttpStatusCode Status, string Text, HttpResponseMessage Response)> SendAsync(
        HttpClient client, HttpMethod method, string url, string? json = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var r = await client.SendAsync(req);
        return (r.StatusCode, await r.Content.ReadAsStringAsync(), r);
    }

    public static string? ErrorCode(string text)
    {
        try
        {
            var root = JsonDocument.Parse(text).RootElement;
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object
                ? e.GetProperty("code").GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    public void Dispose() => Host.Dispose();
}
