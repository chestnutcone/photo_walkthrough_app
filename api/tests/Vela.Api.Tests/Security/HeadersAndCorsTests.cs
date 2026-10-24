using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Tests.Admin;
using Vela.Api.Tests.Share;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Security.SecurityWorld;

namespace Vela.Api.Tests.Security;

[Collection(PostgresCollection.Name)]
public partial class HeadersAndCorsTests(PostgresFixture pg)
{
    private const string Viewer = "https://viewer.example.test"; // Web:ViewerUrl in SecurityWorld.Settings

    [GeneratedRegex(@"script-src\s+([^;]*)")]
    private static partial Regex ScriptSrc();

    // ---- CORS ----

    [Fact]
    public async Task Cors_allows_only_configured_web_viewer_origin()
    {
        var w = await SharedAsync(pg);
        async Task<HttpResponseMessage> Preflight(string origin, string method = "GET", string? headers = "x-platform")
        {
            var req = new HttpRequestMessage(HttpMethod.Options, $"/v1/works/{w.AliceWork}");
            req.Headers.Add("Origin", origin);
            req.Headers.Add("Access-Control-Request-Method", method);
            if (headers is not null) req.Headers.Add("Access-Control-Request-Headers", headers);
            return await w.Anonymous.SendAsync(req);
        }

        var allowed = await Preflight(Viewer);
        Assert.True((int)allowed.StatusCode is 200 or 204, $"preflight from the viewer answered {(int)allowed.StatusCode}");
        Assert.Equal(Viewer, allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());

        foreach (var origin in new[] { "https://evil.example", "http://viewer.example.test", "https://viewer.example.test.evil.example", "null", "https://sub.viewer.example.test" })
        {
            var denied = await Preflight(origin);
            Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"), $"origin {origin} was allowed");
        }
        // The viewer is read-only: writes are not allowed cross-origin.
        foreach (var method in new[] { "POST", "PUT", "PATCH", "DELETE" })
        {
            var write = await Preflight(Viewer, method);
            Assert.False(write.Headers.TryGetValues("Access-Control-Allow-Methods", out var m) && m.Any(v => v.Contains(method)), $"{method} allowed cross-origin");
        }
    }

    [Fact]
    public async Task Cors_never_uses_a_wildcard_and_never_allows_credentials()
    {
        var w = await SharedAsync(pg);
        foreach (var origin in new[] { Viewer, "https://evil.example" })
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"/v1/works/{w.AliceWork}");
            req.Headers.Add("Origin", origin);
            var r = await w.Anonymous.SendAsync(req);
            if (r.Headers.TryGetValues("Access-Control-Allow-Origin", out var vals)) Assert.DoesNotContain("*", vals);
            Assert.False(r.Headers.Contains("Access-Control-Allow-Credentials"));
        }
        var actual = new HttpRequestMessage(HttpMethod.Get, $"/v1/works/{w.AliceWork}");
        actual.Headers.Add("Origin", Viewer);
        var ok = await w.Anonymous.SendAsync(actual);
        Assert.Equal(Viewer, ok.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("Origin", ok.Headers.Vary);
    }

    [Fact]
    public async Task Cors_headers_never_appear_on_admin_pages()
    {
        using var site = await AdminSite.Create(pg);
        var req = new HttpRequestMessage(HttpMethod.Get, "/admin/login");
        req.Headers.Add("Origin", Viewer);
        var r = await site.Host.NewClient().SendAsync(req);
        Assert.False(r.Headers.Contains("Access-Control-Allow-Origin"));
    }

    // ---- headers on HTML pages ----

    private async Task<List<(string Name, Page Page)>> HtmlPages(SecurityWorld w)
    {
        using var site = await AdminSite.Create(pg);
        var pages = new List<(string, Page)>
        {
            ("share", await w.Anonymous.Fetch($"/s/{w.AliceWork}")),
            ("share-404", await w.Anonymous.Fetch($"/s/{Guid.NewGuid()}")),
            ("legal-privacy", await w.Anonymous.Fetch("/legal/privacy")),
            ("legal-terms", await w.Anonymous.Fetch("/legal/terms")),
            ("legal-dmca", await w.Anonymous.Fetch("/legal/dmca")),
            ("legal-404", await w.Anonymous.Fetch("/legal/nothing")),
            ("admin-login", await site.Host.NewClient().Fetch("/admin/login")),
            ("admin-redirect", await site.Host.NewClient().Fetch("/admin")),
        };
        var admin = await site.AdminBrowser();
        pages.Add(("admin-dashboard", await admin.Get("/admin")));
        pages.Add(("admin-users", await admin.Get("/admin/users")));
        return pages;
    }

    [Fact]
    public async Task Security_headers_present_on_html_pages()
    {
        var w = await SharedAsync(pg);
        foreach (var (name, page) in await HtmlPages(w))
        {
            var csp = page.Header("Content-Security-Policy");
            Assert.True(csp.Length > 0, $"{name}: no CSP");
            Assert.Contains("default-src 'none'", csp);
            Assert.Contains("frame-ancestors 'none'", csp);
            Assert.Contains("base-uri 'none'", csp);
            Assert.DoesNotContain("unsafe-inline", csp);
            Assert.DoesNotContain("unsafe-eval", csp);
            // No script is allowed at all; styles only with the per-response nonce.
            Assert.False(ScriptSrc().IsMatch(csp) && !ScriptSrc().Match(csp).Groups[1].Value.Contains("nonce-"), $"{name}: script-src without nonce");
            var nonce = Regex.Match(csp, "style-src 'nonce-([^']+)'");
            if (page.Status != HttpStatusCode.Redirect && page.Body.Contains("<style")) Assert.True(nonce.Success && page.Body.Contains($"nonce=\"{nonce.Groups[1].Value}\""), $"{name}: style nonce mismatch");
            Assert.Equal("nosniff", page.Header("X-Content-Type-Options"));
            Assert.Equal("DENY", page.Header("X-Frame-Options"));
            Assert.Contains(page.Header("Referrer-Policy"), new[] { "no-referrer", "strict-origin-when-cross-origin" });
            Assert.Contains("camera=()", page.Header("Permissions-Policy"));
            Assert.DoesNotContain("<script", page.Body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task The_style_nonce_changes_on_every_response()
    {
        var w = await SharedAsync(pg);
        var a = (await w.Anonymous.Fetch($"/s/{w.AliceWork}")).Header("Content-Security-Policy");
        var b = (await w.Anonymous.Fetch($"/s/{w.AliceWork}")).Header("Content-Security-Policy");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task Hsts_is_sent_on_every_response_outside_Development_and_Test()
    {
        var w = await SecurityWorld.CreateAsync(pg, new() { ["Security:Hsts"] = "true" });
        foreach (var path in new[] { $"/s/{w.AliceWork}", "/legal/privacy", $"/v1/works/{w.AliceWork}", "/health" })
        {
            var r = await w.Anonymous.Fetch(path);
            var hsts = r.Header("Strict-Transport-Security");
            Assert.Contains("max-age=31536000", hsts);
            Assert.Contains("includeSubDomains", hsts);
        }
        w.Dispose();
    }

    [Fact]
    public void Hsts_defaults_on_outside_Development_and_Test_and_off_inside()
    {
        Assert.True(Vela.Api.Common.SecurityHeaders.HstsEnabled(null, "Production"));
        Assert.True(Vela.Api.Common.SecurityHeaders.HstsEnabled(null, "Staging"));
        Assert.False(Vela.Api.Common.SecurityHeaders.HstsEnabled(null, "Development"));
        Assert.False(Vela.Api.Common.SecurityHeaders.HstsEnabled(null, "Test"));
        Assert.True(Vela.Api.Common.SecurityHeaders.HstsEnabled("true", "Test"));
        Assert.False(Vela.Api.Common.SecurityHeaders.HstsEnabled("false", "Production"));
    }

    [Fact]
    public async Task Json_responses_carry_nosniff()
    {
        var w = await SharedAsync(pg);
        foreach (var path in new[] { $"/v1/works/{w.AliceWork}", "/v1/feed?tab=discover", "/health", "/no/route" })
            Assert.Equal("nosniff", (await w.Anonymous.Fetch(path)).Header("X-Content-Type-Options"));
        Assert.Equal("nosniff", (await w.Anonymous.Fetch("/v1/me")).Header("X-Content-Type-Options"));
    }
}
