using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Abuse.AbuseKit;

namespace Vela.Api.Tests.Abuse;

[Collection(PostgresCollection.Name)]
public class RateLimitTests(PostgresFixture pg) : IDisposable
{
    private readonly List<ProfileTestHost> _hosts = [];
    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private async Task<ProfileTestHost> NewHost(Dictionary<string, string>? settings = null, bool useHeaderIp = true)
    {
        var h = await Host(pg, settings, useHeaderIp);
        _hosts.Add(h);
        return h;
    }

    [Fact]
    public async Task Sixth_email_code_request_for_same_email_in_an_hour_is_429_with_retry_after()
    {
        var c = (await NewHost()).Anonymous();
        var email = NewEmail();
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await EmailRequest(c, email)).Status);

        var r = await EmailRequest(c, email);

        Assert.Equal(HttpStatusCode.TooManyRequests, r.Status);
        Assert.Equal("rate_limited", r.ErrorCode);
        Assert.True(r.Response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task Twenty_first_request_from_same_ip_is_429()
    {
        var c = (await NewHost()).Anonymous();
        var ip = NewIp();
        for (var i = 0; i < 20; i++) Assert.Equal(HttpStatusCode.OK, (await EmailRequest(c, NewEmail(), ip)).Status);

        var r = await EmailRequest(c, NewEmail(), ip);

        Assert.Equal(HttpStatusCode.TooManyRequests, r.Status);
        Assert.Equal("rate_limited", r.ErrorCode);
        Assert.True(r.Response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(HttpStatusCode.OK, (await EmailRequest(c, NewEmail(), NewIp())).Status);
    }

    [Fact]
    public async Task Spoofed_cf_connecting_ip_cannot_dodge_the_ip_limit_end_to_end()
    {
        var c = (await NewHost(useHeaderIp: false)).Anonymous();
        for (var i = 0; i < 20; i++)
            Assert.Equal(HttpStatusCode.OK, (await EmailRequest(c, NewEmail(), tweak: m => m.Headers.Add("CF-Connecting-IP", NewIp()))).Status);

        var r = await EmailRequest(c, NewEmail(), tweak: m => m.Headers.Add("CF-Connecting-IP", NewIp()));

        Assert.Equal(HttpStatusCode.TooManyRequests, r.Status);
    }

    [Fact]
    public async Task Rate_limited_response_uses_envelope()
    {
        var c = (await NewHost(new() { ["Abuse:RateLimits:anon-read:Permit"] = "2" })).Anonymous();
        var ip = NewIp();
        for (var i = 0; i < 2; i++) await Send(c, HttpMethod.Get, "/v1/artists/nobody", ip: ip);

        var r = await Send(c, HttpMethod.Get, "/v1/artists/nobody", ip: ip);

        Assert.Equal(HttpStatusCode.TooManyRequests, r.Status);
        Assert.Equal("application/json", r.Response.Content.Headers.ContentType?.MediaType);
        Assert.False(r.Json.GetProperty("success").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, r.Json.GetProperty("data").ValueKind);
        Assert.Equal("rate_limited", r.Json.GetProperty("error").GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(r.Json.GetProperty("error").GetProperty("message").GetString()));
        Assert.True(int.Parse(r.Response.Headers.GetValues("Retry-After").Single()) is > 0 and <= 60);
    }

    [Fact]
    public async Task Anonymous_reads_are_limited_to_120_a_minute_per_ip()
    {
        var c = (await NewHost()).Anonymous();
        var ip = NewIp();
        for (var i = 0; i < 120; i++) Assert.NotEqual(HttpStatusCode.TooManyRequests, (await Send(c, HttpMethod.Get, "/v1/artists/nobody", ip: ip)).Status);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(c, HttpMethod.Get, "/v1/artists/nobody", ip: ip)).Status);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await Send(c, HttpMethod.Get, "/v1/artists/nobody", ip: NewIp())).Status);
    }

    [Fact]
    public async Task Health_checks_are_never_limited()
    {
        var c = (await NewHost(new() { ["Abuse:RateLimits:anon-read:Permit"] = "1" })).Anonymous();
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await Send(c, HttpMethod.Get, "/health", ip: "10.9.9.9")).Status);
    }

    [Fact]
    public async Task Sixty_first_write_in_a_minute_by_one_user_is_429_and_other_users_are_unaffected()
    {
        var h = await NewHost();
        var busy = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var other = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        // Onboarding was the first write of the minute.
        for (var i = 0; i < 59; i++) Assert.NotEqual(HttpStatusCode.TooManyRequests, (await busy.Delete($"/v1/works/{Guid.NewGuid()}")).Status);

        var r = await busy.Delete($"/v1/works/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.TooManyRequests, r.Status);
        Assert.Equal("rate_limited", r.ErrorCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await other.Delete($"/v1/works/{Guid.NewGuid()}")).Status);
    }

    [Fact]
    public async Task Twenty_first_upload_ticket_in_an_hour_by_one_user_is_429()
    {
        var h = await NewHost();
        var u = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        for (var i = 0; i < 20; i++) Assert.Equal(HttpStatusCode.OK, (await u.Post("/v1/me/avatar-upload", new { })).Status);

        var r = await u.Post("/v1/me/avatar-upload", new { });

        Assert.Equal(HttpStatusCode.TooManyRequests, r.Status);
        Assert.Equal("rate_limited", r.ErrorCode);
    }

    [Fact]
    public async Task Sixth_public_form_post_from_one_ip_in_an_hour_is_429()
    {
        var h = await NewHost();
        var first = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var second = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var buyer = await h.SignIn();
        var ip = NewIp();
        var body = System.Text.Json.JsonSerializer.Serialize(new { type = "Commission", email = "b@example.test", message = "hi" });
        // The inquiry service allows 3 per sender per artist, so spread five sends over two artists.
        foreach (var artist in new[] { first, first, first, second, second })
            Assert.Equal(HttpStatusCode.OK, (await Send(buyer.Client, HttpMethod.Post, $"/v1/artists/{artist.Id}/inquiries", body, ip)).Status);

        var r = await Send(buyer.Client, HttpMethod.Post, $"/v1/artists/{second.Id}/inquiries", body, ip);

        Assert.Equal(HttpStatusCode.TooManyRequests, r.Status);
        Assert.Equal("rate_limited", r.ErrorCode);
    }

    [Fact]
    public async Task Limit_partitions_do_not_leak_between_policies()
    {
        var c = (await NewHost(new() { ["Abuse:RateLimits:anon-read:Permit"] = "1" })).Anonymous();
        var ip = NewIp();
        await Send(c, HttpMethod.Get, "/v1/artists/nobody", ip: ip);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(c, HttpMethod.Get, "/v1/artists/nobody", ip: ip)).Status);

        Assert.Equal(HttpStatusCode.OK, (await EmailRequest(c, NewEmail(), ip)).Status);
    }
}
