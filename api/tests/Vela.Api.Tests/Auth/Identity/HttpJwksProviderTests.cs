using System.Net;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Vela.Api.Auth.Identity;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Auth.Identity;

public class HttpJwksProviderTests
{
    private sealed class Handler(Func<string> body) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body()) });
        }
    }

    private static string Jwks(params string[] kids)
    {
        var keys = kids.Select(k =>
        {
            var p = System.Security.Cryptography.RSA.Create(2048).ExportParameters(false);
            return new { kty = "RSA", use = "sig", alg = "RS256", kid = k,
                n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent) };
        });
        return JsonSerializer.Serialize(new { keys });
    }

    [Fact]
    public async Task Caches_keys_and_refreshes_once_per_minute_on_unknown_kid()
    {
        var kids = new[] { "a" };
        var h = new Handler(() => Jwks(kids));
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var p = new HttpJwksProvider(new HttpClient(h), clock);

        Assert.NotNull(await p.GetKeyAsync("http://t/jwks", "a"));
        Assert.NotNull(await p.GetKeyAsync("http://t/jwks", "a"));
        Assert.Equal(1, h.Calls);

        Assert.Null(await p.GetKeyAsync("http://t/jwks", "b")); // within a minute: no refetch
        Assert.Equal(1, h.Calls);

        kids = ["a", "b"];
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.NotNull(await p.GetKeyAsync("http://t/jwks", "b")); // rotation picked up
        Assert.Equal(2, h.Calls);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        public int Calls;
        public bool Fail = true;
        public string Body = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            return Fail ? throw new HttpRequestException("provider down") : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) });
        }
    }

    [Fact]
    public async Task A_failed_fetch_starts_the_cool_down_so_an_outage_costs_one_attempt_a_minute()
    {
        var h = new FailingHandler();
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var p = new HttpJwksProvider(new HttpClient(h), clock);

        await Assert.ThrowsAsync<HttpRequestException>(() => p.GetKeyAsync("http://t/jwks", "a"));
        Assert.Null(await p.GetKeyAsync("http://t/jwks", "a"));      // within the minute: no new request
        Assert.Null(await p.GetKeyAsync("http://t/jwks", "a"));
        Assert.Equal(1, h.Calls);

        clock.Advance(TimeSpan.FromMinutes(2));
        h.Fail = false; h.Body = Jwks("a");
        Assert.NotNull(await p.GetKeyAsync("http://t/jwks", "a"));   // recovered
        Assert.Equal(2, h.Calls);
    }

    [Fact]
    public async Task A_duplicate_kid_in_the_key_set_does_not_break_the_lookup()
    {
        var p = new HttpJwksProvider(new HttpClient(new Handler(() => Jwks("a", "a", "b"))), new MutableTimeProvider(DateTimeOffset.UtcNow));
        Assert.NotNull(await p.GetKeyAsync("http://t/jwks", "a"));
        Assert.NotNull(await p.GetKeyAsync("http://t/jwks", "b"));
    }

    [Fact]
    public async Task One_provider_being_slow_does_not_block_the_other()
    {
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var handler = new GateHandler(started, release);
        var p = new HttpJwksProvider(new HttpClient(handler), new MutableTimeProvider(DateTimeOffset.UtcNow));

        var slow = p.GetKeyAsync("http://slow/jwks", "a");
        await started.Task;
        var fast = await p.GetKeyAsync("http://fast/jwks", "a").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(fast);

        release.SetResult();
        Assert.NotNull(await slow);
    }

    private sealed class GateHandler(TaskCompletionSource started, TaskCompletionSource release) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            if (r.RequestUri!.Host == "slow")
            {
                started.TrySetResult();
                await release.Task;
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Jwks("a")) };
        }
    }
}
