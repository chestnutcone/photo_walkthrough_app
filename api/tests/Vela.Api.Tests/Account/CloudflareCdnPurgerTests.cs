using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vela.Api.Account;

namespace Vela.Api.Tests.Account;

public class CloudflareCdnPurgerTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request, body));
            return respond(request, body);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static readonly HttpResponseMessage Unused = Json(HttpStatusCode.OK, "{}");
    private static HttpResponseMessage Ok() => Json(HttpStatusCode.OK, """{"success":true,"errors":[],"result":{"id":"x"}}""");

    private static CloudflareCdnPurger Purger(FakeHandler h) =>
        new(new HttpClient(h), new CloudflareOptions("zone123", "tok-abc", "https://vela.example"), NullLogger<CloudflareCdnPurger>.Instance);

    [Fact]
    public async Task Posts_files_to_the_zone_with_a_bearer_token()
    {
        var h = new FakeHandler((_, _) => Ok());
        await Purger(h).PurgeAsync(["https://img.vela.example/works/a/thumb.webp"]);

        var (req, body) = Assert.Single(h.Calls);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("https://api.cloudflare.com/client/v4/zones/zone123/purge_cache", req.RequestUri!.ToString());
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("tok-abc", req.Headers.Authorization.Parameter);
        Assert.Equal(["https://img.vela.example/works/a/thumb.webp"], JsonDocument.Parse(body).RootElement.GetProperty("files").EnumerateArray().Select(e => e.GetString()!).ToArray());
    }

    [Fact]
    public async Task Trailing_slash_urls_are_purged_as_prefixes_without_a_scheme()
    {
        var h = new FakeHandler((_, _) => Ok());
        await Purger(h).PurgeAsync(["https://img.vela.example/works/a/"]);

        var json = JsonDocument.Parse(Assert.Single(h.Calls).Body).RootElement;
        Assert.False(json.TryGetProperty("files", out _));
        Assert.Equal(["img.vela.example/works/a/"], json.GetProperty("prefixes").EnumerateArray().Select(e => e.GetString()!).ToArray());
    }

    [Fact]
    public async Task Relative_paths_are_resolved_against_the_share_host()
    {
        var h = new FakeHandler((_, _) => Ok());
        await Purger(h).PurgeAsync(["/s/abc"]);

        var files = JsonDocument.Parse(Assert.Single(h.Calls).Body).RootElement.GetProperty("files");
        Assert.Equal("https://vela.example/s/abc", files[0].GetString());
    }

    [Fact]
    public async Task Requests_are_split_into_batches_of_thirty()
    {
        var h = new FakeHandler((_, _) => Ok());
        var urls = Enumerable.Range(0, 65).Select(i => $"https://img.vela.example/works/{i}/full.webp").ToList();
        await Purger(h).PurgeAsync(urls);

        Assert.Equal([30, 30, 5], h.Calls.Select(c => JsonDocument.Parse(c.Body).RootElement.GetProperty("files").GetArrayLength()).ToArray());
    }

    [Fact]
    public async Task An_empty_list_makes_no_request()
    {
        var h = new FakeHandler((_, _) => Ok());
        await Purger(h).PurgeAsync([]);
        Assert.Empty(h.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"success":false,"errors":[{"code":10000,"message":"Authentication error"}]}""")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"success":false,"errors":[]}""")]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.OK, """{"success":false,"errors":[{"code":1,"message":"nope"}]}""")]
    public async Task A_failed_purge_throws_so_the_caller_retries(HttpStatusCode code, string body)
    {
        var h = new FakeHandler((_, _) => Json(code, body));
        await Assert.ThrowsAsync<HttpRequestException>(() => Purger(h).PurgeAsync(["https://img.vela.example/works/a/full.webp"]));
    }

    [Fact]
    public async Task A_later_batch_failing_still_throws()
    {
        var n = 0;
        var h = new FakeHandler((_, _) => ++n == 2 ? Json(HttpStatusCode.BadGateway, "") : Ok());
        var urls = Enumerable.Range(0, 40).Select(i => $"https://img.vela.example/works/{i}/full.webp").ToList();
        await Assert.ThrowsAsync<HttpRequestException>(() => Purger(h).PurgeAsync(urls));
    }

    [Fact]
    public async Task The_error_never_leaks_the_token_or_urls()
    {
        var h = new FakeHandler((_, _) => Json(HttpStatusCode.Forbidden, "{}"));
        var e = await Assert.ThrowsAsync<HttpRequestException>(() => Purger(h).PurgeAsync(["https://img.vela.example/works/secret-id/full.webp"]));
        Assert.DoesNotContain("tok-abc", e.ToString());
        Assert.DoesNotContain("secret-id", e.Message);
    }

    // ---- wiring ----

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static IConfiguration Cfg(params (string, string)[] kv) =>
        new ConfigurationBuilder().AddInMemoryCollection(kv.ToDictionary(k => k.Item1, k => (string?)k.Item2)).Build();

    private static ServiceProvider Build(IConfiguration cfg, string env = "Production")
    {
        var s = new ServiceCollection().AddLogging();
        s.AddSingleton<ICdnPurger, NoOpCdnPurger>();
        s.AddCdnPurging(cfg, new Env(env));
        return s.BuildServiceProvider();
    }

    [Fact]
    public void Configured_zone_and_token_select_the_cloudflare_purger()
    {
        using var sp = Build(Cfg(("Cloudflare:ZoneId", "z"), ("Cloudflare:ApiToken", "t"), ("Branding:Domain", "vela.example")));
        Assert.IsType<CloudflareCdnPurger>(sp.GetRequiredService<ICdnPurger>());
    }

    [Fact]
    public void Without_cloudflare_settings_the_noop_purger_stays()
    {
        using var sp = Build(Cfg());
        Assert.IsType<NoOpCdnPurger>(sp.GetRequiredService<ICdnPurger>());
    }

    [Theory]
    [InlineData("Cloudflare:ZoneId")]
    [InlineData("Cloudflare:ApiToken")]
    public void Half_configured_cloudflare_fails_startup_naming_the_missing_key(string present)
    {
        var missing = present == "Cloudflare:ZoneId" ? "Cloudflare:ApiToken" : "Cloudflare:ZoneId";
        var e = Assert.Throws<InvalidOperationException>(() => Build(Cfg((present, "x"))));
        Assert.Contains(missing, e.Message);
    }

    [Fact]
    public void Production_with_the_noop_purger_logs_a_warning()
    {
        var logs = new CapturingLoggerProvider();
        using var lf = LoggerFactory.Create(b => b.AddProvider(logs));
        CdnPurgingExtensions.WarnIfNotConfigured(new NoOpCdnPurger(NullLogger<NoOpCdnPurger>.Instance), new Env("Production"), lf.CreateLogger("t"));
        Assert.Contains(logs.Lines, l => l.Contains("Cloudflare:ZoneId"));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void Dev_and_test_do_not_warn(string env)
    {
        var logs = new CapturingLoggerProvider();
        using var lf = LoggerFactory.Create(b => b.AddProvider(logs));
        CdnPurgingExtensions.WarnIfNotConfigured(new NoOpCdnPurger(NullLogger<NoOpCdnPurger>.Instance), new Env(env), lf.CreateLogger("t"));
        Assert.Empty(logs.Lines);
    }
}
