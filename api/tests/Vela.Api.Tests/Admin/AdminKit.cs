using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Account;
using Vela.Api.Admin;
using Vela.Api.Tests.Account;
using Vela.Api.Common;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Share;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Admin;

public sealed class MutableAllowList(IEnumerable<string> emails) : IAdminAllowList
{
    private readonly HashSet<string> _set = [.. emails];
    public bool Any => _set.Count > 0;
    public bool Contains(string normalizedEmail) => _set.Contains(normalizedEmail);
    public void Remove(string email) => _set.Remove(email);
    public void Add(string email) => _set.Add(email);
}

public sealed class FixedIp(string ip) : IClientIpResolver
{
    public IPAddress Resolve(HttpContext ctx) => IPAddress.Parse(ip);
}

/// <summary>A running API with one allow-listed admin address and a fresh client IP (so email rate limits never collide).</summary>
public sealed class AdminSite : IDisposable
{
    public const string Pepper = "test-ban-pepper-0123456789-abcdefghijkl";
    public string AdminEmail { get; } = $"boss-{Guid.NewGuid():N}@vela-admin.test";
    public MutableAllowList Allow { get; }
    public ProfileTestHost Host { get; }
    public RecordingCdnPurger Cdn { get; } = new();
    public string? ConnectionString { get; }

    private AdminSite(PostgresFixture pg, string? cs, Action<IServiceCollection>? extra, bool noAdmins)
    {
        ConnectionString = cs;
        Allow = new MutableAllowList(noAdmins ? [] : [AdminEmail]);
        var ip = $"10.{Random.Shared.Next(256)}.{Random.Shared.Next(256)}.{Random.Shared.Next(1, 255)}";
        Host = new ProfileTestHost(pg, new Dictionary<string, string> { ["Admin:BanPepper"] = Pepper }, cs, s =>
        {
            s.AddSingleton<IAdminAllowList>(Allow);
            s.AddSingleton<IClientIpResolver>(new FixedIp(ip));
            s.AddSingleton<ICdnPurger>(Cdn);
            extra?.Invoke(s);
        });
    }

    public static async Task<AdminSite> Create(PostgresFixture pg, bool isolated = false, Action<IServiceCollection>? extra = null, bool noAdmins = false) =>
        new(pg, isolated ? await pg.CreateIsolatedDatabaseAsync() : null, extra, noAdmins);

    public Browser NewBrowser() => new(Host.NewClient(), Host);

    /// <summary>A signed-in admin browser.</summary>
    public async Task<Browser> AdminBrowser()
    {
        var b = NewBrowser();
        await b.LoginAsync(AdminEmail);
        return b;
    }

    public async Task<Guid> AdminId()
    {
        await using var db = Host.Db();
        return await db.Identities.Where(i => i.Provider == "email" && i.Subject == AdminEmail).Select(i => i.UserId).SingleAsync();
    }

    public void Dispose() => Host.Dispose();
}

public sealed partial class Browser(HttpClient client, ProfileTestHost host)
{
    public HttpClient Client => client;

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    public Task<Page> Get(string url) => client.Fetch(url);

    public static string? TokenIn(string html) => TokenPattern().Match(html).Groups[1].Success ? TokenPattern().Match(html).Groups[1].Value : null;

    public async Task<string> TokenAsync(string fromUrl = "/admin/login")
    {
        var page = await Get(fromUrl);
        return TokenIn(page.Body) ?? throw new InvalidOperationException($"no antiforgery token at {fromUrl} ({page.Status})");
    }

    public async Task<Page> Post(string url, IDictionary<string, string> form, bool withToken = true, string tokenFrom = "/admin/login")
    {
        var fields = new Dictionary<string, string>(form);
        if (withToken) fields["__RequestVerificationToken"] = await TokenAsync(tokenFrom);
        var r = await client.PostAsync(url, new FormUrlEncodedContent(fields));
        return new Page(r.StatusCode, await r.Content.ReadAsStringAsync(), r);
    }

    public string LatestCode(string email)
    {
        var mail = host.Services.GetRequiredService<FakeEmailSender>().Sent.Last(m => m.To == email);
        return Regex.Match(mail.TextBody, @"\b(\d{6})\b").Groups[1].Value;
    }

    public async Task<Page> LoginAsync(string email)
    {
        var first = await Post("/admin/login", new Dictionary<string, string> { ["Email"] = email });
        Assert.Equal(HttpStatusCode.OK, first.Status);
        var done = await Post("/admin/login?handler=Verify", new Dictionary<string, string> { ["Email"] = email, ["Code"] = LatestCode(email) });
        Assert.Equal(HttpStatusCode.Redirect, done.Status);
        return done;
    }

    public async Task<bool> IsSignedIn()
    {
        var p = await Get("/admin");
        return p.Status == HttpStatusCode.OK;
    }
}
