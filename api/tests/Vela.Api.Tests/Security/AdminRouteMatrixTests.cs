using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Tests.Admin;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Security;

/// <summary>
/// Every route under /admin (found by enumerating the running endpoint table, so new pages are covered automatically) and every
/// POST handler on it must refuse anonymous callers, API users and former admins. Only the login page may answer an anonymous GET.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AdminRouteMatrixTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _site = null!;
    public async Task InitializeAsync() => _site = await AdminSite.Create(pg);
    public Task DisposeAsync() { _site.Dispose(); return Task.CompletedTask; }

    private sealed record AdminRoute(string Pattern, string Url, IReadOnlyList<string> PostHandlers);

    private List<AdminRoute> Routes()
    {
        var filled = new Regex(@"\{(\w+)(:[^}]*)?\??\}");
        var found = new List<AdminRoute>();
        foreach (var e in _site.Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var raw = Normalise(e.RoutePattern.RawText);
            if (!raw.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)) continue;
            var handlers = e.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.RazorPages.CompiledPageActionDescriptor>()?.ModelTypeInfo?.AsType()
                is { } model ? PostHandlers(model) : [];
            found.Add(new AdminRoute(raw, filled.Replace(raw, _ => Guid.NewGuid().ToString()), handlers));
        }
        return found;
    }

    /// <summary>Razor Pages report their route without the leading slash.</summary>
    private static string Normalise(string? raw) => "/" + (raw ?? "").TrimStart('/');

    private static IReadOnlyList<string> PostHandlers(Type model) =>
        model.GetMethods().Select(m => Regex.Match(m.Name, @"^OnPost(\w*?)(Async)?$")).Where(m => m.Success)
            .Select(m => m.Groups[1].Value).ToList();

    private static bool IsLogin(AdminRoute r) => r.Pattern.Equals("/admin/login", StringComparison.OrdinalIgnoreCase);

    private static void AssertRefused(string who, string method, string url, HttpResponseMessage r, string body, bool anonymousLoginGet = false)
    {
        if (anonymousLoginGet) { Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound, $"{who}: {url} answered {(int)r.StatusCode}"); return; } // 404 when no admin address is configured at all
        var ok = r.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound
            or HttpStatusCode.BadRequest;
        Assert.True(ok, $"{who}: {method} {url} answered {(int)r.StatusCode}");
        if (r.StatusCode == HttpStatusCode.Redirect)
            Assert.Equal("/admin/login", r.Headers.Location?.OriginalString);
        Assert.DoesNotContain("Review queue", body);
    }

    [Fact]
    public void The_admin_area_is_not_empty_and_has_post_handlers_to_check()
    {
        var routes = Routes();
        Assert.True(routes.Count >= 8, $"expected the admin pages in the endpoint table, found {routes.Count}");
        Assert.Contains(routes, r => r.PostHandlers.Count > 0);
    }

    [Fact]
    public async Task Anonymous_callers_get_no_admin_page_and_no_admin_action()
    {
        var anon = _site.Host.NewClient();
        foreach (var route in Routes())
        {
            var get = await anon.GetAsync(route.Url);
            AssertRefused("anonymous", "GET", route.Url, get, await get.Content.ReadAsStringAsync(), anonymousLoginGet: IsLogin(route));
            foreach (var handler in route.PostHandlers.Prepend(""))
            {
                var url = handler.Length == 0 ? route.Url : $"{route.Url}?handler={handler}";
                var post = await anon.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string> { ["Reason"] = "x" }));
                AssertRefused("anonymous", "POST", url, post, await post.Content.ReadAsStringAsync());
            }
        }
    }

    [Fact]
    public async Task Signed_in_api_users_with_a_valid_bearer_token_get_nothing_from_the_admin_area()
    {
        var user = await _site.Host.SignInOnboarded(Profiles.ProfileTestHost.UniqueHandle("nm"));
        await using (var db = _site.Host.Db()) // even an IsAdmin flag in the database is not a credential for these pages
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsAdmin, true));
        var api = _site.Host.NewClient(); // does not follow redirects, so a refusal is seen as the redirect it is
        api.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", SecurityWorld.TokenOf(user));
        foreach (var route in Routes())
        {
            var get = await api.GetAsync(route.Url);
            AssertRefused("api user", "GET", route.Url, get, await get.Content.ReadAsStringAsync(), anonymousLoginGet: IsLogin(route));
            foreach (var handler in route.PostHandlers.Prepend(""))
            {
                var url = handler.Length == 0 ? route.Url : $"{route.Url}?handler={handler}";
                var post = await api.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string> { ["Reason"] = "x" }));
                AssertRefused("api user", "POST", url, post, await post.Content.ReadAsStringAsync());
            }
        }
    }

    [Fact]
    public async Task A_signed_in_user_whose_email_is_not_on_the_admin_allow_list_gets_nothing_even_with_an_old_admin_cookie()
    {
        var browser = await _site.AdminBrowser();
        Assert.True(await browser.IsSignedIn());
        _site.Allow.Remove(_site.AdminEmail); // the address left the allow-list; the session cookie is still in the jar

        foreach (var route in Routes())
        {
            var get = await browser.Client.GetAsync(route.Url);
            AssertRefused("ex-admin", "GET", route.Url, get, await get.Content.ReadAsStringAsync(), anonymousLoginGet: IsLogin(route));
            foreach (var handler in route.PostHandlers.Prepend(""))
            {
                var url = handler.Length == 0 ? route.Url : $"{route.Url}?handler={handler}";
                var post = await browser.Client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string> { ["Reason"] = "x" }));
                AssertRefused("ex-admin", "POST", url, post, await post.Content.ReadAsStringAsync());
            }
        }
    }

    [Fact]
    public async Task The_login_flow_never_signs_in_an_address_that_is_not_on_the_allow_list_even_with_a_valid_code()
    {
        var stranger = $"stranger-{Guid.NewGuid():N}@example.test";
        var api = _site.Host.Anonymous();
        Assert.Equal(HttpStatusCode.OK, (await api.PostAsJsonAsync("/v1/auth/email/request", new { email = stranger })).StatusCode);
        var code = _site.NewBrowser().LatestCode(stranger); // a real, valid code for the app

        var browser = _site.NewBrowser();
        var verify = await browser.Post("/admin/login?handler=Verify", new Dictionary<string, string> { ["Email"] = stranger, ["Code"] = code });

        Assert.NotEqual(HttpStatusCode.Redirect, verify.Status);
        Assert.False(await browser.IsSignedIn());
    }

    [Fact]
    public async Task Every_endpoint_outside_the_contract_is_either_a_known_public_page_or_requires_authorization()
    {
        var known = new[] { "/health", "/s/{workId}", "/legal/{slug}", "/.well-known/apple-app-site-association", "/.well-known/assetlinks.json" };
        var contractNames = SecurityContract.Operations.Select(o => o.OperationId).ToHashSet();
        var endpoints = _site.Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        var unexplained = new List<string>();
        foreach (var e in endpoints)
        {
            var raw = Normalise(e.RoutePattern.RawText);
            var name = e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IEndpointNameMetadata>()?.EndpointName;
            if (name is not null && contractNames.Contains(name)) continue;
            if (raw.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
            {
                var authorized = e.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any();
                var anonymous = e.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is not null;
                if (!authorized && !(anonymous && IsLoginRaw(raw))) unexplained.Add($"{raw} (admin route without the admin policy)");
                continue;
            }
            if (known.Any(k => string.Equals(k, raw, StringComparison.OrdinalIgnoreCase))) continue;
            unexplained.Add(raw);
        }
        Assert.True(unexplained.Count == 0, "Routes with no documented security decision: " + string.Join(", ", unexplained));
        await Task.CompletedTask;
    }

    private static bool IsLoginRaw(string raw) => raw.Equals("/admin/login", StringComparison.OrdinalIgnoreCase);
}
