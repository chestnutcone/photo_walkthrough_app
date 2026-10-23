using System.Net;
using System.Text;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Abuse.AbuseKit;
using Raw = Vela.Api.Tests.Abuse.Raw;

namespace Vela.Api.Tests.Abuse;

[Collection(PostgresCollection.Name)]
public class RequestLimitTests(PostgresFixture pg) : IDisposable
{
    private readonly List<ProfileTestHost> _hosts = [];
    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private async Task<HttpClient> Client()
    {
        var h = await Host(pg);
        _hosts.Add(h);
        return h.Anonymous();
    }

    private static string Padded(int bytes) => "{\"email\":\"" + new string('a', bytes) + "\"}";

    [Fact]
    public async Task Oversized_json_body_is_413()
    {
        var c = await Client();

        var r = await Send(c, HttpMethod.Post, "/v1/auth/email/request", Padded(1_048_576), NewIp());

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.Status);
        Assert.Equal("payload_too_large", r.ErrorCode);
        Assert.False(r.Json.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Oversized_chunked_json_body_is_413_without_a_content_length()
    {
        var c = await Client();
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/email/request")
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(Padded(1_200_000)))),
        };
        req.Content.Headers.ContentType = new("application/json");
        req.Headers.TransferEncodingChunked = true;
        req.Headers.Add("X-Test-Ip", NewIp());

        var r = await c.SendAsync(req);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
        Assert.Contains("payload_too_large", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Body_just_under_one_megabyte_is_accepted_through_to_the_endpoint()
    {
        var c = await Client();

        var r = await Send(c, HttpMethod.Post, "/v1/auth/email/request", Padded(1_000_000), NewIp());

        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Equal("invalid_email", r.ErrorCode);
    }

    [Fact]
    public async Task Deeply_nested_json_is_400()
    {
        var c = await Client();
        var deep = "{\"email\":\"a@example.test\",\"x\":" + new string('[', 40) + new string(']', 40) + "}";

        var r = await Send(c, HttpMethod.Post, "/v1/auth/email/request", deep, NewIp());

        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Equal("invalid_json", r.ErrorCode);
    }

    [Fact]
    public async Task Pathologically_deep_json_is_400_not_a_crash()
    {
        var c = await Client();
        var deep = new string('[', 200_000) + new string(']', 200_000);

        var r = await Send(c, HttpMethod.Post, "/v1/auth/email/request", deep, NewIp());

        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Equal("invalid_json", r.ErrorCode);
    }

    [Fact]
    public async Task Json_nested_exactly_32_deep_is_allowed()
    {
        var c = await Client();
        var ok = "{\"email\":\"a@example.test\",\"x\":" + new string('[', 31) + new string(']', 31) + "}";

        var r = await Send(c, HttpMethod.Post, "/v1/auth/email/request", ok, NewIp());

        Assert.Equal(HttpStatusCode.OK, r.Status);
    }

    [Fact]
    public async Task Malformed_json_still_reaches_the_endpoint_for_its_own_error()
    {
        var c = await Client();
        var r = await Send(c, HttpMethod.Post, "/v1/auth/email/request", "{not json", NewIp());
        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.DoesNotContain("invalid_json", r.Text);
    }

    private static async Task<Raw> Raw(HttpClient c, HttpMethod m, string url, byte[] body, string? contentType, bool chunked = false)
    {
        var req = new HttpRequestMessage(m, url) { Content = chunked ? new StreamContent(new MemoryStream(body)) : new ByteArrayContent(body) };
        req.Content.Headers.ContentType = contentType is null ? null : new(contentType);
        if (chunked) req.Headers.TransferEncodingChunked = true;
        req.Headers.Add("X-Test-Ip", NewIp());
        var r = await c.SendAsync(req);
        return new Raw(r, await r.Content.ReadAsStringAsync());
    }

    private static byte[] Big() => Encoding.UTF8.GetBytes("{\"displayName\":\"" + new string('a', 1_200_000) + "\"}");
    private static byte[] Deep() => Encoding.UTF8.GetBytes("{\"x\":" + new string('[', 40) + new string(']', 40) + "}");

    public static IEnumerable<object?[]> BodyShapes() =>
    [
        ["text/plain", false], [null, false], ["application/octet-stream", false],
        ["text/plain", true], [null, true], ["application/json", true],
    ];

    [Theory]
    [MemberData(nameof(BodyShapes))]
    public async Task Oversized_body_is_413_whatever_the_content_type_or_framing(string? contentType, bool chunked)
    {
        var h = await Host(pg); _hosts.Add(h);
        var u = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var artist = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var routes = new (HttpMethod, string)[]
        {
            (HttpMethod.Patch, "/v1/me/profile"),
            (HttpMethod.Post, $"/v1/artists/{artist.Id}/inquiries"),
            (HttpMethod.Post, "/v1/reports"),
            (HttpMethod.Post, "/v1/notifications/read"),
            (HttpMethod.Post, $"/v1/works/{Guid.NewGuid()}/chapters/0/replies"),
        };
        foreach (var (method, url) in routes)
        {
            var r = await Raw(u.Client, method, url, Big(), contentType, chunked);
            Assert.True(r.Status == HttpStatusCode.RequestEntityTooLarge, $"{method} {url} {contentType} chunked={chunked} gave {(int)r.Status}");
            Assert.Equal("payload_too_large", r.ErrorCode);
        }
    }

    [Theory]
    [MemberData(nameof(BodyShapes))]
    public async Task Deeply_nested_body_is_400_whatever_the_content_type_or_framing(string? contentType, bool chunked)
    {
        var h = await Host(pg); _hosts.Add(h);
        var u = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var artist = await h.SignInOnboarded(ProfileTestHost.UniqueHandle());
        foreach (var (method, url) in new (HttpMethod, string)[]
                 { (HttpMethod.Patch, "/v1/me/profile"), (HttpMethod.Post, $"/v1/artists/{artist.Id}/inquiries") })
        {
            var r = await Raw(u.Client, method, url, Deep(), contentType, chunked);
            Assert.True(r.Status == HttpStatusCode.BadRequest, $"{method} {url} {contentType} chunked={chunked} gave {(int)r.Status}");
            Assert.Equal("invalid_json", r.ErrorCode);
        }
    }

    [Fact]
    public async Task Bodyless_requests_are_untouched()
    {
        var c = await Client();
        Assert.NotEqual(HttpStatusCode.RequestEntityTooLarge, (await c.GetAsync("/v1/artists/nobody")).StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, (await c.PostAsync("/v1/me/avatar-upload", null)).StatusCode);
    }

    [Fact]
    public async Task Disposable_email_domain_is_refused_at_email_request()
    {
        var c = await Client();

        var r = await Send(c, HttpMethod.Post, "/v1/auth/email/request", "{\"email\":\"someone@mailinator.com\"}", NewIp());

        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Equal("disposable_email", r.ErrorCode);
    }

    [Fact]
    public async Task Configured_disposable_domains_are_refused_too()
    {
        var h = await Host(pg, new() { ["Abuse:DisposableEmailDomains:0"] = "burner.example" });
        _hosts.Add(h);

        var r = await Send(h.Anonymous(), HttpMethod.Post, "/v1/auth/email/request", "{\"email\":\"x@burner.example\"}", NewIp());

        Assert.Equal("disposable_email", r.ErrorCode);
    }
}

[Collection(PostgresCollection.Name)]
public class EmailNormalisationTests(PostgresFixture pg) : IDisposable
{
    private readonly List<ProfileTestHost> _hosts = [];
    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    [Theory]
    [InlineData("x@mailinator.com.")]
    [InlineData("x@Inbox.Mailinator.COM.")]
    [InlineData("x@mailinator.com...")]
    public async Task Trailing_dot_does_not_dodge_the_disposable_list(string email)
    {
        var h = await Host(pg); _hosts.Add(h);
        var r = await EmailRequest(h.Anonymous(), email);
        Assert.Equal("disposable_email", r.ErrorCode);
    }

    [Theory]
    [InlineData("x@a..example.test")]
    [InlineData("x@.example.test")]
    [InlineData("x@.")]
    [InlineData("x@exa mple.test")]
    public async Task Malformed_domains_are_invalid_emails(string email)
    {
        var h = await Host(pg); _hosts.Add(h);
        var r = await EmailRequest(h.Anonymous(), email);
        Assert.Equal("invalid_email", r.ErrorCode);
    }

    [Fact]
    public async Task Per_email_limit_counts_the_address_with_and_without_a_trailing_dot_together()
    {
        var h = await Host(pg); _hosts.Add(h);
        var c = h.Anonymous();
        var email = NewEmail();
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await EmailRequest(c, i % 2 == 0 ? email : email + ".")).Status);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await EmailRequest(c, email + ".")).Status);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await EmailRequest(c, email)).Status);
    }
}
