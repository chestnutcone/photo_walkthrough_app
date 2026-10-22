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
}
