using System.Net;
using System.Text.RegularExpressions;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Share.ShareTestKit;

namespace Vela.Api.Tests.Share;

[Collection(PostgresCollection.Name)]
public class LegalTests(PostgresFixture pg)
{
    private ProfileTestHost Host(params (string, string)[] extra)
    {
        var d = new Dictionary<string, string>(Config);
        foreach (var (k, v) in extra) d[k] = v;
        return new ProfileTestHost(pg, d);
    }

    [Theory]
    [InlineData("privacy", "sub-processors")]
    [InlineData("terms", "18")]
    [InlineData("content", "counter-notice")]
    [InlineData("dmca", "designated agent")]
    public async Task Legal_pages_render_and_are_marked_draft(string slug, string topic)
    {
        using var host = Host();

        var p = await host.Anonymous().Fetch($"/legal/{slug}");

        Assert.Equal(HttpStatusCode.OK, p.Status);
        Assert.Equal("text/html; charset=utf-8", p.Header("Content-Type"));
        Assert.Contains("DRAFT: pending legal review", p.Body);
        Assert.Contains(topic, p.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Brandy", p.Body);
        Assert.DoesNotContain("Vela", p.Body);
        Assert.DoesNotContain("{{", p.Body);
        Assert.Contains("placeholder", p.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", p.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("style-src 'nonce-[^']+'", p.Header("Content-Security-Policy"));
        Assert.Equal("DENY", p.Header("X-Frame-Options"));
        Assert.Equal("nosniff", p.Header("X-Content-Type-Options"));
    }

    [Fact]
    public async Task Privacy_page_covers_the_headline_topics()
    {
        using var host = Host();

        var body = (await host.Anonymous().Fetch("/legal/privacy")).Body.ToLowerInvariant();

        foreach (var t in new[] { "data we collect", "sub-processors", "retention", "deletion", "export", "evidence", "18" })
            Assert.Contains(t, body);
    }

    [Fact]
    public async Task Terms_and_content_pages_cover_the_headline_topics()
    {
        using var host = Host();
        var terms = (await host.Anonymous().Fetch("/legal/terms")).Body.ToLowerInvariant();
        var content = (await host.Anonymous().Fetch("/legal/content")).Body.ToLowerInvariant();

        foreach (var t in new[] { "18", "warrant", "license", "prohibited", "termination" }) Assert.Contains(t, terms);
        foreach (var t in new[] { "takedown", "counter-notice", "repeat infringer" }) Assert.Contains(t, content);
    }

    [Fact]
    public async Task Unknown_legal_slug_is_404()
    {
        using var host = Host();
        var client = host.Anonymous();

        Assert.Equal(HttpStatusCode.NotFound, (await client.Fetch("/legal/nope")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await client.Fetch("/legal/..%2Fprivacy")).Status);
    }

    [Fact]
    public async Task Dmca_page_shows_a_not_configured_placeholder_without_agent_settings()
    {
        using var host = Host();

        var p = await host.Anonymous().Fetch("/legal/dmca");

        Assert.Contains("not yet configured", p.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dmca_page_shows_the_configured_agent_encoded()
    {
        using var host = Host(("Legal:DmcaAgentName", "Agent <b>Smith</b>"), ("Legal:DmcaAgentEmail", "dmca@example.test"),
            ("Legal:DmcaAgentAddress", "1 Main St \"Suite\" 2"));

        var p = await host.Anonymous().Fetch("/legal/dmca");

        Assert.Contains("Agent &lt;b&gt;Smith&lt;/b&gt;", p.Body);
        Assert.Contains("dmca@example.test", p.Body);
        Assert.Contains("1 Main St &quot;Suite&quot; 2", p.Body);
        Assert.DoesNotContain("<b>Smith", p.Body);
        Assert.DoesNotContain("not yet configured", p.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Branding_comes_only_from_options_and_raw_html_in_the_name_is_encoded()
    {
        using var host = Host(("Branding:AppName", "Evil<script>alert(1)</script>"));

        var p = await host.Anonymous().Fetch("/legal/terms");

        Assert.DoesNotContain("<script", p.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Evil&lt;script&gt;", p.Body);
    }
}
