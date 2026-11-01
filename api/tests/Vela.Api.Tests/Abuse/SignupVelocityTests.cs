using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Abuse;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Abuse.AbuseKit;

namespace Vela.Api.Tests.Abuse;

[Collection(PostgresCollection.Name)]
public class SignupVelocityTests(PostgresFixture pg) : IDisposable
{
    private readonly List<ProfileTestHost> _hosts = [];
    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private async Task<ProfileTestHost> NewHost(string? perDay = "5")
    {
        var settings = perDay is null ? null : new Dictionary<string, string> { ["Abuse:SignupsPerIpPerDay"] = perDay };
        var h = await Host(pg, settings);
        _hosts.Add(h);
        return h;
    }

    private static Task<Raw> NewAccount(HttpClient c, string ip, string? subject = null) =>
        Send(c, HttpMethod.Post, "/v1/auth/sso",
            JsonSerializer.Serialize(new { provider = "fake", idToken = "fake:" + (subject ?? "sv-" + Guid.NewGuid()), nonce = "n" }), ip);

    [Fact]
    public async Task Sixth_new_account_from_same_ip_in_a_day_is_refused()
    {
        var c = (await NewHost()).Anonymous();
        var ip = NewIp();
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await NewAccount(c, ip)).Status);

        var r = await NewAccount(c, ip);

        Assert.Equal(HttpStatusCode.TooManyRequests, r.Status);
        Assert.Equal("rate_limited", r.ErrorCode);
        Assert.True(r.Response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(HttpStatusCode.OK, (await NewAccount(c, NewIp())).Status);
    }

    [Fact]
    public async Task Refused_signup_creates_no_user()
    {
        var h = await NewHost("1");
        var c = h.Anonymous();
        var ip = NewIp();
        await NewAccount(c, ip);
        var subject = "refused-" + Guid.NewGuid();

        await NewAccount(c, ip, subject);

        await using var db = h.Db();
        Assert.False(await db.Identities.AnyAsync(i => i.Subject == subject));
    }

    [Fact]
    public async Task Signing_in_to_an_existing_account_does_not_count()
    {
        var c = (await NewHost("1")).Anonymous();
        var ip = NewIp();
        var subject = "again-" + Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await NewAccount(c, ip, subject)).Status);

        for (var i = 0; i < 4; i++) Assert.Equal(HttpStatusCode.OK, (await NewAccount(c, ip, subject)).Status);
    }

    [Fact]
    public async Task Window_slides_after_a_day()
    {
        var h = await NewHost("1");
        var c = h.Anonymous();
        var ip = NewIp();
        await NewAccount(c, ip);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await NewAccount(c, ip)).Status);

        h.Clock.Advance(TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1));

        Assert.Equal(HttpStatusCode.OK, (await NewAccount(c, ip)).Status);
    }

    [Fact]
    public async Task Concurrent_signups_cannot_exceed_the_limit()
    {
        var c = (await NewHost()).Anonymous();
        var ip = NewIp();

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => NewAccount(c, ip)));

        Assert.Equal(5, results.Count(r => r.Status == HttpStatusCode.OK));
        Assert.Equal(7, results.Count(r => r.Status == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task Concurrent_first_sign_ins_of_one_identity_count_once()
    {
        var h = await NewHost("2");
        var c = h.Anonymous();
        var ip = NewIp();
        var subject = "race-" + Guid.NewGuid();

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => NewAccount(c, ip, subject)));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Equal(HttpStatusCode.OK, (await NewAccount(c, ip)).Status);
        await using var db = h.Db();
        Assert.Equal(2, await db.SignupEvents.CountAsync());
    }

    [Fact]
    public async Task Ipv6_addresses_in_one_slash_64_share_a_budget()
    {
        var c = (await NewHost("2")).Anonymous();
        var net = $"2001:db8:{Random.Shared.Next(0xffff):x}:{Random.Shared.Next(0xffff):x}";
        Assert.Equal(HttpStatusCode.OK, (await NewAccount(c, $"{net}::1")).Status);
        Assert.Equal(HttpStatusCode.OK, (await NewAccount(c, $"{net}:aaaa::2")).Status);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await NewAccount(c, $"{net}:0:0:0:3")).Status);
    }

    [Fact]
    public async Task Email_code_signups_count_too()
    {
        var h = await NewHost("1");
        var c = h.Anonymous();
        var ip = NewIp();
        await NewAccount(c, ip);
        var email = NewEmail();
        await EmailRequest(c, email, ip);
        var code = System.Text.RegularExpressions.Regex.Match(
            h.Services.GetRequiredService<Vela.Api.Common.FakeEmailSender>().Sent.Last().TextBody, @"\b\d{6}\b").Value;

        var r = await Send(c, HttpMethod.Post, "/v1/auth/email/verify", JsonSerializer.Serialize(new { email, code }), ip);

        Assert.Equal(HttpStatusCode.TooManyRequests, r.Status);
        Assert.Equal("rate_limited", r.ErrorCode);
    }

    [Fact]
    public async Task Old_events_are_pruned_when_a_new_one_is_recorded()
    {
        var h = await NewHost("5");
        var c = h.Anonymous();
        await NewAccount(c, NewIp());
        h.Clock.Advance(TimeSpan.FromHours(30));

        await NewAccount(c, NewIp());

        await using var db = h.Db();
        Assert.Equal(1, await db.SignupEvents.CountAsync());
    }

    [Fact]
    public async Task Allow_is_a_read_only_check_that_does_not_record()
    {
        var h = await NewHost("1");
        using var scope = h.Scope();
        var v = scope.ServiceProvider.GetRequiredService<SignupVelocity>();
        var ip = IPAddress.Parse(NewIp());

        Assert.True(await v.AllowAsync(ip));
        Assert.True(await v.AllowAsync(ip));
        await using var db = h.Db();
        Assert.Equal(0, await db.SignupEvents.CountAsync());
    }
}
