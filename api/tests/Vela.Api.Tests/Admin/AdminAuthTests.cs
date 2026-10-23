using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Vela.Api.Admin;
using Vela.Api.Common;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Share;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Admin;

[Collection(PostgresCollection.Name)]
public class AdminAuthTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _site = null!;
    public async Task InitializeAsync() => _site = await AdminSite.Create(pg);
    public Task DisposeAsync() { _site.Dispose(); return Task.CompletedTask; }

    private static string Strip(string html) =>
        Regex.Replace(Regex.Replace(html, "value=\"[^\"]{40,}\"", "value=\"X\""), "nonce=\"[^\"]+\"", "nonce=\"N\"");

    [Fact]
    public async Task Admin_login_gives_the_same_response_for_listed_and_unlisted_emails_and_only_mails_listed_ones()
    {
        var b = _site.NewBrowser();
        var outbox = _site.Host.Services.GetRequiredService<FakeEmailSender>();
        var stranger = $"nobody-{Guid.NewGuid():N}@example.test";

        var a = await b.Post("/admin/login", new Dictionary<string, string> { ["Email"] = _site.AdminEmail });
        var c = await b.Post("/admin/login", new Dictionary<string, string> { ["Email"] = stranger });

        Assert.Equal(a.Status, c.Status);
        Assert.Equal(Strip(a.Body).Replace(_site.AdminEmail, "E"), Strip(c.Body).Replace(stranger, "E"));
        Assert.Single(outbox.Sent, m => m.To == _site.AdminEmail);
        Assert.DoesNotContain(outbox.Sent, m => m.To == stranger);
    }

    [Fact]
    public async Task Correct_code_for_a_listed_email_signs_in_makes_the_user_admin_and_sets_a_locked_down_cookie()
    {
        var b = _site.NewBrowser();
        var done = await b.LoginAsync(_site.AdminEmail);

        var cookie = done.Response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("vela_admin="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/admin", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.True(await b.IsSignedIn());
        await using var db = _site.Host.Db();
        Assert.True(await db.Users.AnyAsync(u => u.Id == _site.AdminId().Result && u.IsAdmin));
    }

    [Fact]
    public void Cookie_is_Secure_everywhere_except_Development_and_Test()
    {
        Assert.Equal(Microsoft.AspNetCore.Http.CookieSecurePolicy.Always, AdminAuth.SecurePolicy(new FakeEnv("Production")));
        Assert.Equal(Microsoft.AspNetCore.Http.CookieSecurePolicy.Always, AdminAuth.SecurePolicy(new FakeEnv("Staging")));
        Assert.Equal(Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest, AdminAuth.SecurePolicy(new FakeEnv("Development")));
        Assert.Equal(Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest, AdminAuth.SecurePolicy(new FakeEnv("Test")));
    }

    private sealed class FakeEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "x";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public async Task Wrong_code_and_unlisted_email_with_a_valid_code_both_fail_without_a_cookie_and_without_consuming_the_code()
    {
        var b = _site.NewBrowser();
        await b.Post("/admin/login", new Dictionary<string, string> { ["Email"] = _site.AdminEmail });
        var wrong = await b.Post("/admin/login?handler=Verify", new Dictionary<string, string> { ["Email"] = _site.AdminEmail, ["Code"] = "000000" });
        Assert.Equal(HttpStatusCode.OK, wrong.Status);
        Assert.False(wrong.Response.Headers.Contains("Set-Cookie") && wrong.Response.Headers.GetValues("Set-Cookie").Any(c => c.StartsWith("vela_admin=")));

        // An outsider gets a valid code through the public API for their own address, then tries it at the admin door.
        var outsider = $"outsider-{Guid.NewGuid():N}@example.test";
        var api = _site.Host.Anonymous();
        Assert.Equal(HttpStatusCode.OK, (await api.PostAsJsonAsync("/v1/auth/email/request", new { email = outsider })).StatusCode);
        var code = b.LatestCode(outsider);
        var r = await b.Post("/admin/login?handler=Verify", new Dictionary<string, string> { ["Email"] = outsider, ["Code"] = code });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.False(await b.IsSignedIn());
        var verify = await api.PostAsJsonAsync("/v1/auth/email/verify", new { email = outsider, code });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode); // the admin door did not burn the code
        await using var db = _site.Host.Db();
        Assert.False(await db.Users.AnyAsync(u => u.IsAdmin && db.Identities.Any(i => i.UserId == u.Id && i.Subject == outsider)));
    }

    [Fact]
    public async Task Session_expires_after_eight_hours_absolute_and_is_not_extended_by_activity()
    {
        var b = _site.NewBrowser();
        await b.LoginAsync(_site.AdminEmail);
        _site.Host.Clock.Advance(TimeSpan.FromHours(7));
        Assert.True(await b.IsSignedIn());
        _site.Host.Clock.Advance(TimeSpan.FromMinutes(59));
        Assert.True(await b.IsSignedIn());
        _site.Host.Clock.Advance(TimeSpan.FromMinutes(2));
        var p = await b.Get("/admin");
        Assert.Equal(HttpStatusCode.Redirect, p.Status);
        Assert.Equal("/admin/login", p.Response.Headers.Location?.OriginalString);
    }

    public static IEnumerable<object[]> Demotions() =>
    [
        ["demoted"], ["deleted"], ["suspended"], ["removed_from_allow_list"], ["user_row_gone"],
    ];

    [Theory]
    [MemberData(nameof(Demotions))]
    public async Task Cookie_is_rechecked_on_every_request(string how)
    {
        var b = _site.NewBrowser();
        await b.LoginAsync(_site.AdminEmail);
        Assert.True(await b.IsSignedIn());
        var id = await _site.AdminId();
        await using (var db = _site.Host.Db())
        {
            var q = db.Users.Where(u => u.Id == id);
            switch (how)
            {
                case "demoted": await q.ExecuteUpdateAsync(s => s.SetProperty(u => u.IsAdmin, false)); break;
                case "deleted": await q.ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAt, ProfileTestHost.Now)); break;
                case "suspended": await q.ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, ProfileTestHost.Now)); break;
                case "removed_from_allow_list": _site.Allow.Remove(_site.AdminEmail); break;
                case "user_row_gone": await q.ExecuteDeleteAsync(); break;
            }
        }
        var p = await b.Get("/admin");
        Assert.NotEqual(HttpStatusCode.OK, p.Status);
        Assert.DoesNotContain("Review queue", p.Body);
    }

    [Fact]
    public async Task Login_revokes_only_the_token_family_it_created_so_the_apps_session_survives()
    {
        // The same person already uses the app with an email-code session.
        var api = _site.Host.Anonymous();
        await api.PostAsJsonAsync("/v1/auth/email/request", new { email = _site.AdminEmail });
        var code = _site.NewBrowser().LatestCode(_site.AdminEmail);
        var verify = await api.PostAsJsonAsync("/v1/auth/email/verify", new { email = _site.AdminEmail, code });
        var appRefresh = System.Text.Json.JsonDocument.Parse(await verify.Content.ReadAsStringAsync()).RootElement
            .GetProperty("data").GetProperty("refreshToken").GetString();

        await _site.NewBrowser().LoginAsync(_site.AdminEmail);

        var id = await _site.AdminId();
        await using var db = _site.Host.Db();
        var rows = await db.RefreshTokens.Where(t => t.UserId == id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows.Count(t => t.RevokedAt is null));
        var refreshed = await api.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = appRefresh });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
    }

    [Fact]
    public async Task Admin_only_login_leaves_no_usable_api_token_behind()
    {
        await _site.NewBrowser().LoginAsync(_site.AdminEmail);
        var id = await _site.AdminId();
        await using var db = _site.Host.Db();
        Assert.All(await db.RefreshTokens.Where(t => t.UserId == id).ToListAsync(), t => Assert.NotNull(t.RevokedAt));
    }

    [Fact]
    public async Task Posts_without_an_antiforgery_token_are_rejected()
    {
        var b = _site.NewBrowser();
        var noToken = await b.Post("/admin/login", new Dictionary<string, string> { ["Email"] = _site.AdminEmail }, withToken: false);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.Status);
        var verify = await b.Post("/admin/login?handler=Verify", new Dictionary<string, string> { ["Email"] = _site.AdminEmail, ["Code"] = "123456" }, withToken: false);
        Assert.Equal(HttpStatusCode.BadRequest, verify.Status);

        var admin = await _site.AdminBrowser();
        var logout = await admin.Post("/admin/logout", new Dictionary<string, string>(), withToken: false, tokenFrom: "/admin");
        Assert.Equal(HttpStatusCode.BadRequest, logout.Status);
        Assert.True(await admin.IsSignedIn());
    }

    [Fact]
    public async Task Logout_clears_the_session()
    {
        var b = await _site.AdminBrowser();
        var r = await b.Post("/admin/logout", new Dictionary<string, string>(), tokenFrom: "/admin");
        Assert.Equal(HttpStatusCode.Redirect, r.Status);
        Assert.False(await b.IsSignedIn());
    }

    [Fact]
    public async Task Bearer_tokens_do_not_open_admin_pages_and_the_admin_cookie_does_not_open_the_api()
    {
        var user = await _site.Host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var withBearer = await user.Client.Fetch("/admin"); // this client follows redirects
        Assert.Equal("/admin/login", withBearer.Response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("Review queue", withBearer.Body);

        var b = await _site.AdminBrowser();
        Assert.True(await b.IsSignedIn());
        // The jar holds vela_admin; forward it to the API by hand too.
        var jar = (await b.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/admin"))).RequestMessage!.Headers;
        var raw = jar.TryGetValues("Cookie", out var v) ? string.Join("; ", v) : "";
        Assert.Contains("vela_admin=", raw);
        var api = _site.Host.Anonymous();
        var req = new HttpRequestMessage(HttpMethod.Get, "/v1/me");
        req.Headers.Add("Cookie", raw);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Every_mapped_admin_route_requires_the_AdminCookie_policy_except_login()
    {
        var sources = _site.Host.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Select(e => (Path: (e.RoutePattern.RawText ?? "").TrimStart('/').ToLowerInvariant(), E: e))
            .Where(x => x.Path == "admin" || x.Path.StartsWith("admin/")).ToList();
        Assert.True(sources.Count >= 9, $"expected the admin pages to be mapped, found {sources.Count}");
        foreach (var (path, e) in sources)
        {
            var policies = e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
            if (path == "admin/login")
            {
                Assert.NotEmpty(e.Metadata.GetOrderedMetadata<IAllowAnonymous>());
                continue;
            }
            Assert.True(policies.Contains(AdminAuth.Policy), $"/{path} is missing the {AdminAuth.Policy} policy");
            Assert.Empty(e.Metadata.GetOrderedMetadata<IAllowAnonymous>());
        }
        foreach (var expected in new[] { "admin", "admin/login", "admin/logout", "admin/audit", "admin/users", "admin/users/{id:guid}", "admin/reports/{id:guid}", "admin/evidence", "admin/evidence/{id:guid}" })
            Assert.Contains(sources, x => x.Path == expected);
    }

    [Fact]
    public async Task Admin_pages_send_no_store_security_headers_and_a_per_request_style_nonce()
    {
        var b = _site.NewBrowser();
        var one = await b.Get("/admin/login");
        var two = await b.Get("/admin/login");
        Assert.Equal("no-store", one.Header("Cache-Control"));
        Assert.Equal("nosniff", one.Header("X-Content-Type-Options"));
        Assert.Equal("no-referrer", one.Header("Referrer-Policy"));
        Assert.Equal("DENY", one.Header("X-Frame-Options"));
        Assert.Contains("camera=()", one.Header("Permissions-Policy"));

        string Nonce(Page p) => Regex.Match(p.Header("Content-Security-Policy"), "style-src 'nonce-([^']+)'").Groups[1].Value;
        Assert.NotEmpty(Nonce(one));
        Assert.NotEqual(Nonce(one), Nonce(two));
        Assert.Equal(
            $"default-src 'none'; img-src 'self' https://blob.test; style-src 'nonce-{Nonce(one)}'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'",
            one.Header("Content-Security-Policy"));
        Assert.Contains($"<style nonce=\"{Nonce(one)}\">", one.Body);
        Assert.DoesNotContain("<script", one.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" style=\"", one.Body);
    }

    [Fact]
    public async Task Admin_pages_answer_404_when_no_admin_email_is_configured()
    {
        using var closed = await AdminSite.Create(pg, noAdmins: true);
        var b = closed.NewBrowser();
        Assert.Equal(HttpStatusCode.NotFound, (await b.Get("/admin")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Get("/admin/login")).Status);
    }

    [Fact]
    public void Startup_validation_demands_admin_emails_and_a_long_ban_pepper_outside_Development_and_Test()
    {
        static IConfiguration Cfg(params (string, string)[] kv) => new ConfigurationBuilder().AddInMemoryCollection(kv.ToDictionary(x => x.Item1, x => (string?)x.Item2)).Build();
        var prod = new FakeEnv("Production");

        var noEmails = Assert.Throws<InvalidOperationException>(() => AdminSettings.Load(Cfg(("Admin:BanPepper", new string('p', 40))), prod));
        Assert.Contains("Admin:Emails", noEmails.Message);
        var shortPepper = Assert.Throws<InvalidOperationException>(() => AdminSettings.Load(Cfg(("Admin:Emails:0", "a@b.test"), ("Admin:BanPepper", "short")), prod));
        Assert.Contains("Admin:BanPepper", shortPepper.Message);
        var noPepper = Assert.Throws<InvalidOperationException>(() => AdminSettings.Load(Cfg(("Admin:Emails:0", "a@b.test")), prod));
        Assert.Contains("Admin:BanPepper", noPepper.Message);

        var ok = AdminSettings.Load(Cfg(("Admin:Emails", "A@B.test, c@d.test"), ("Admin:BanPepper", new string('p', 32))), prod);
        Assert.True(ok.Enabled);
        Assert.Equal(new string('p', 32), ok.BanPepper);
        Assert.True(new ConfigAdminAllowList(Cfg(("Admin:Emails", "A@B.test, c@d.test"))).Contains("a@b.test"));
        Assert.False(AdminSettings.Load(Cfg(("Admin:Enabled", "false")), prod).Enabled);
        Assert.True(AdminSettings.Load(Cfg(), new FakeEnv("Development")).BanPepper.Length >= 32);
    }
}
