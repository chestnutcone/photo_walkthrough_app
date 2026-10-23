using System.Net;
using System.Text;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Abuse.AbuseKit;

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

    [Fact]
    public async Task Non_json_bodies_are_not_subject_to_the_json_limit()
    {
        var c = await Client();
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/me/avatar-upload")
        {
            Content = new ByteArrayContent(new byte[1_500_000]),
        };
        req.Content.Headers.ContentType = new("application/octet-stream");
        var r = await c.SendAsync(req);
        Assert.NotEqual(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
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
