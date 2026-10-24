using System.Net.Http.Json;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Security.SecurityWorld;

namespace Vela.Api.Tests.Security;

/// <summary>S1b-13: private API answers must never be stored by a shared cache (one "cache everything" CDN rule would leak them).</summary>
[Collection(PostgresCollection.Name)]
public class CacheControlTests(PostgresFixture pg)
{
    private static bool NoStore(HttpResponseMessage r) => r.Headers.CacheControl?.NoStore == true;
    private static bool VariesByAuthorization(HttpResponseMessage r) => r.Headers.Vary.Contains("Authorization", StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task Authenticated_api_responses_are_no_store_and_vary_by_authorization()
    {
        var w = await SharedAsync(pg);
        foreach (var url in new[] { "/v1/me", "/v1/notifications", $"/v1/me/export/{w.AliceExport}", $"/v1/works/{w.AliceDraft}", $"/v1/works/{w.AliceWork}" })
        {
            var r = await w.Alice.Client.GetAsync(url);
            Assert.True(NoStore(r), $"{url} ({(int)r.StatusCode}) lacks no-store");
            Assert.True(VariesByAuthorization(r), url);
        }
    }

    [Fact]
    public async Task Token_responses_and_other_writes_are_no_store_even_without_a_bearer_token()
    {
        var w = await SharedAsync(pg);
        var r = await w.Anonymous.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = "nope" });
        Assert.True(NoStore(r));
    }

    [Fact]
    public async Task Anonymous_public_reads_and_share_pages_stay_cacheable()
    {
        var w = await SharedAsync(pg);
        var work = await w.Anonymous.GetAsync($"/v1/works/{w.AliceWork}");
        Assert.False(NoStore(work));
        Assert.True(VariesByAuthorization(work)); // a cache must not hand this copy to a signed-in caller
        var share = await w.Anonymous.GetAsync($"/s/{w.AliceWork}");
        Assert.False(NoStore(share));
    }
}
