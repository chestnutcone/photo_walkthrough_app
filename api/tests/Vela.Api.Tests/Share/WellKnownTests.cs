using System.Net;
using System.Text.Json;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Share.ShareTestKit;

namespace Vela.Api.Tests.Share;

[Collection(PostgresCollection.Name)]
public class WellKnownTests(PostgresFixture pg)
{
    private ProfileTestHost Host(params (string, string)[] extra)
    {
        var d = new Dictionary<string, string>(Config);
        foreach (var (k, v) in extra) d[k] = v;
        return new ProfileTestHost(pg, d);
    }

    [Fact]
    public async Task Well_known_files_reflect_configuration()
    {
        using var host = Host(("Links:AppleTeamId", "ABCDE12345"), ("Links:BundleId", "com.example.brandy"),
            ("Links:AndroidPackage", "com.example.brandy"), ("Links:Sha256Fingerprints:0", "AA:BB"));
        var client = host.Anonymous();

        var aasa = await client.Fetch("/.well-known/apple-app-site-association");
        var links = await client.Fetch("/.well-known/assetlinks.json");

        Assert.Equal(HttpStatusCode.OK, aasa.Status);
        Assert.Equal("application/json", aasa.Header("Content-Type").Split(';')[0]);
        Assert.Equal("""{"applinks":{"details":[{"appIDs":["ABCDE12345.com.example.brandy"],"components":[{"/":"/s/*"}]}]}}""", aasa.Body);
        Assert.Equal(HttpStatusCode.OK, links.Status);
        Assert.Equal("application/json", links.Header("Content-Type").Split(';')[0]);
        var stmt = JsonDocument.Parse(links.Body).RootElement[0];
        Assert.Equal("delegate_permission/common.handle_all_urls", stmt.GetProperty("relation")[0].GetString());
        Assert.Equal("android_app", stmt.GetProperty("target").GetProperty("namespace").GetString());
        Assert.Equal("com.example.brandy", stmt.GetProperty("target").GetProperty("package_name").GetString());
        Assert.Equal(["AA:BB"], stmt.GetProperty("target").GetProperty("sha256_cert_fingerprints").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Assetlinks_lists_multiple_fingerprints()
    {
        using var host = Host(("Links:AndroidPackage", "com.example.brandy"),
            ("Links:Sha256Fingerprints:0", "AA:BB"), ("Links:Sha256Fingerprints:1", "CC:DD"));

        var links = await host.Anonymous().Fetch("/.well-known/assetlinks.json");

        var fps = JsonDocument.Parse(links.Body).RootElement[0].GetProperty("target").GetProperty("sha256_cert_fingerprints");
        Assert.Equal(["AA:BB", "CC:DD"], fps.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Unconfigured_well_known_files_are_404()
    {
        using var host = Host();
        var client = host.Anonymous();

        Assert.Equal(HttpStatusCode.NotFound, (await client.Fetch("/.well-known/apple-app-site-association")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await client.Fetch("/.well-known/assetlinks.json")).Status);
    }

    [Theory]
    [InlineData("Links:AppleTeamId", "ABCDE12345")]
    [InlineData("Links:BundleId", "com.example.brandy")]
    public async Task Partly_configured_apple_file_is_404(string key, string value)
    {
        using var host = Host((key, value));

        Assert.Equal(HttpStatusCode.NotFound, (await host.Anonymous().Fetch("/.well-known/apple-app-site-association")).Status);
    }

    [Fact]
    public async Task Assetlinks_without_fingerprints_or_package_is_404()
    {
        using var noFp = Host(("Links:AndroidPackage", "com.example.brandy"));
        using var noPkg = Host(("Links:Sha256Fingerprints:0", "AA:BB"));

        Assert.Equal(HttpStatusCode.NotFound, (await noFp.Anonymous().Fetch("/.well-known/assetlinks.json")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await noPkg.Anonymous().Fetch("/.well-known/assetlinks.json")).Status);
    }
}
