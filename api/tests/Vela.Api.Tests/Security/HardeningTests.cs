using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Abuse;
using Vela.Api.Auth;
using Vela.Api.Tests.Legal;
using Vela.Api.Tests.Share;
using Vela.Api.Common;
using Vela.Api.Legal;
using Vela.Api.Tests.Abuse;
using Vela.Api.Tests.Admin;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Security.SecurityWorld;

namespace Vela.Api.Tests.Security;

/// <summary>Fixes for findings deferred by the per-task reviews (see docs/security/review-findings.md), each with the test that pins it.</summary>
[Collection(PostgresCollection.Name)]
public class HardeningTests(PostgresFixture pg)
{
    // ---- A2: a replay race must not leave a live token in a revoked session ----

    [Fact]
    public async Task Racing_replays_of_one_refresh_token_end_with_the_whole_session_revoked()
    {
        var w = await SecurityWorld.CreateAsync(pg);
        for (var round = 0; round < 12; round++)
        {
            var sub = "p-" + Guid.NewGuid();
            var (_, text, _) = await SendAsync(w.Anonymous, HttpMethod.Post, "/v1/auth/sso", $"{{\"provider\":\"fake\",\"idToken\":\"fake:{sub}\",\"nonce\":\"n\"}}");
            var refresh = System.Text.Json.JsonDocument.Parse(text).RootElement.GetProperty("data").GetProperty("refreshToken").GetString();

            var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
                SendAsync(w.Host.Anonymous(), HttpMethod.Post, "/v1/auth/refresh", $"{{\"refreshToken\":\"{refresh}\"}}")));

            Assert.Contains(results, r => r.Status == HttpStatusCode.Unauthorized); // at most one can win
            await using var db = w.Host.Db();
            var userId = (await db.Identities.SingleAsync(i => i.Subject == sub)).UserId;
            var live = await db.RefreshTokens.CountAsync(t => t.UserId == userId && t.RevokedAt == null);
            Assert.True(live == 0, $"round {round}: {live} refresh token(s) still live after a replay");
        }
        w.Dispose();
    }

    // ---- A15: non-ASCII email domains ----

    [Theory]
    [InlineData("user@mailinator。com")]      // ideographic full stop looks like a dot to IDNA
    [InlineData("user@mаilinator.com")]      // Cyrillic a
    [InlineData("user@ｍailinator.com")]      // full-width m
    public async Task Email_with_a_non_ascii_domain_is_refused_before_any_code_is_sent(string email)
    {
        using var host = await AbuseKit.Host(pg, new(Settings));
        var outbox = host.Services.GetRequiredService<FakeEmailSender>();

        var r = await AbuseKit.EmailRequest(host.Anonymous(), email);

        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.DoesNotContain(outbox.Sent, m => m.To.Contains("mailinator", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Email_with_a_punycode_domain_still_works()
    {
        using var host = await AbuseKit.Host(pg, new(Settings));
        var r = await AbuseKit.EmailRequest(host.Anonymous(), "reader@xn--bcher-kva.example");
        Assert.Equal(HttpStatusCode.OK, r.Status);
    }

    // ---- A15: signed-in reads and /admin are rate limited ----

    [Fact]
    public async Task Signed_in_reads_are_limited_per_user_not_unlimited()
    {
        var w = await SecurityWorld.CreateAsync(pg, new() { ["Abuse:RateLimits:user-read:Permit"] = "3" });
        var u = await w.NewUserAsync();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++) codes.Add((await u.Get("/v1/feed?tab=discover")).Status);

        Assert.Equal(HttpStatusCode.TooManyRequests, codes[^1]);
        Assert.Equal(HttpStatusCode.OK, codes[0]);
        Assert.Equal(HttpStatusCode.OK, (await w.Alice.Get("/v1/feed?tab=discover")).Status); // another user is unaffected
        w.Dispose();
    }

    [Fact]
    public async Task Admin_pages_are_rate_limited_per_address()
    {
        using var site = await AdminSite.Create(pg, settings: new() { ["Abuse:RateLimits:anon-read:Permit"] = "3", ["Abuse:RateLimits:write:Permit"] = "2" });
        var http = site.Host.NewClient();
        var reads = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++) reads.Add((await http.GetAsync("/admin/login")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, reads[^1]);

        var posts = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++) posts.Add((await http.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = "x@y.test" }))).StatusCode);
        Assert.Contains(HttpStatusCode.TooManyRequests, posts);
    }

    // ---- A14a: admin sign-in answers take the same minimum time for listed and unlisted addresses ----

    [Fact]
    public async Task Admin_sign_in_steps_take_at_least_the_configured_floor_for_listed_and_unlisted_addresses()
    {
        const int floorMs = 350;
        using var site = await AdminSite.Create(pg, settings: new() { ["Admin:LoginMinimumMs"] = floorMs.ToString() });
        var b = site.NewBrowser();
        var stranger = $"nobody-{Guid.NewGuid():N}@example.test";

        async Task<long> Time(string url, Dictionary<string, string> form)
        {
            var token = await b.TokenAsync();
            form["__RequestVerificationToken"] = token;
            var sw = Stopwatch.StartNew();
            await b.Client.PostAsync(url, new FormUrlEncodedContent(form));
            return sw.ElapsedMilliseconds;
        }

        var times = new[]
        {
            await Time("/admin/login", new() { ["Email"] = stranger }),
            await Time("/admin/login", new() { ["Email"] = site.AdminEmail }),
            await Time("/admin/login?handler=Verify", new() { ["Email"] = stranger, ["Code"] = "123456" }),
            await Time("/admin/login?handler=Verify", new() { ["Email"] = site.AdminEmail, ["Code"] = "123456" }),
        };
        Assert.All(times, t => Assert.True(t >= floorMs - 25, $"an admin sign-in answer came back after {t} ms"));
    }

    // ---- A14a: signing out must end the session everywhere, and sign-in/out are audited ----

    private static HttpClient WithCookieCopy(AdminSite site, Page signedIn)
    {
        var set = signedIn.Response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("vela_admin=", StringComparison.Ordinal));
        var copy = site.Host.NewClient();
        copy.DefaultRequestHeaders.Add("Cookie", set.Split(';')[0]);
        return copy;
    }

    [Fact]
    public async Task A_copied_admin_cookie_stops_working_when_the_admin_signs_out()
    {
        using var site = await AdminSite.Create(pg, isolated: true);
        var browser = site.NewBrowser();
        var login = await browser.LoginAsync(site.AdminEmail);
        var stolen = WithCookieCopy(site, login);
        Assert.Equal(HttpStatusCode.OK, (await stolen.GetAsync("/admin")).StatusCode);

        var logout = await browser.Post("/admin/logout", new Dictionary<string, string>(), tokenFrom: "/admin");
        Assert.Equal(HttpStatusCode.Redirect, logout.Status);

        var replay = await stolen.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
        Assert.Equal("/admin/login", replay.Headers.Location?.OriginalString);
        Assert.DoesNotContain("Review queue", await replay.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Signing_in_again_ends_the_earlier_session_so_only_one_cookie_is_ever_live()
    {
        using var site = await AdminSite.Create(pg, isolated: true);
        var first = await site.NewBrowser().LoginAsync(site.AdminEmail);
        var earlier = WithCookieCopy(site, first);
        Assert.Equal(HttpStatusCode.OK, (await earlier.GetAsync("/admin")).StatusCode);

        var second = site.NewBrowser();
        await second.LoginAsync(site.AdminEmail);

        Assert.True(await second.IsSignedIn());
        Assert.Equal(HttpStatusCode.Redirect, (await earlier.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task Admin_sign_in_and_sign_out_are_written_to_the_audit_trail()
    {
        using var site = await AdminSite.Create(pg, isolated: true);
        var browser = await site.AdminBrowser();
        await browser.Post("/admin/logout", new Dictionary<string, string>(), tokenFrom: "/admin");

        await using var db = site.Host.Db();
        var adminId = await site.AdminId();
        var actions = await db.AdminActions.Where(a => a.AdminId == adminId).OrderBy(a => a.At).ThenBy(a => a.Action).Select(a => a.Action).ToListAsync();
        Assert.Contains("admin_login", actions);
        Assert.Contains("admin_logout", actions);
        Assert.DoesNotContain(await db.AdminActions.Select(a => a.Reason).ToListAsync(), r => r.Contains(site.AdminEmail)); // no email in the trail
    }

    // ---- A14b: the DMCA form cannot be used to mail-bomb a third party ----

    [Fact]
    public async Task Acknowledgement_emails_to_one_claimant_address_stop_after_three_a_day_but_notices_are_still_stored()
    {
        var ip = new Vela.Api.Tests.Legal.MutableIp("203.0.113.9");
        using var site = await AdminSite.Create(pg, isolated: true, extra: s => s.AddSingleton<IClientIpResolver>(ip));
        var http = site.Host.Anonymous();
        var victim = $"victim-{Guid.NewGuid():N}@example.test";

        for (var i = 0; i < 5; i++)
        {
            ip.Ip = $"203.0.113.{20 + i}"; // different senders, so the per-address notice limit is not what stops the mail
            var body = Vela.Api.Tests.Legal.DmcaKit.Valid();
            body["claimantEmail"] = victim;
            Assert.Equal(HttpStatusCode.Accepted, (await Vela.Api.Tests.Legal.DmcaKit.PostNotice(http, body)).Status);
        }

        var sent = site.Host.Services.GetRequiredService<FakeEmailSender>().Sent.Count(m => m.To == victim);
        Assert.Equal(DmcaService.MaxAcksPerEmailPerDay, sent);
        Assert.Equal(5, (await site.Notices()).Count(n => n.ClaimantEmail == victim));
    }

    // ---- A4: raw emails and IPs are not kept longer than the limits need them ----

    [Fact]
    public async Task Email_codes_and_verify_attempts_older_than_a_day_are_deleted_and_recent_ones_kept()
    {
        var w = await SecurityWorld.CreateAsync(pg);
        var now = w.Host.Clock.GetUtcNow();
        var oldAt = now - AuthDataRetentionJob.Retention - TimeSpan.FromMinutes(1);
        var freshAt = now - TimeSpan.FromHours(1);
        var tag = $"retention-{Guid.NewGuid():N}@example.test";
        await using (var db = w.Host.Db())
        {
            db.EmailCodes.AddRange(
                new Vela.Api.Data.Entities.EmailCode { Id = Guid.NewGuid(), Email = tag, CodeHash = "h", RequestIp = "203.0.113.1", CreatedAt = oldAt, ExpiresAt = oldAt.AddMinutes(10) },
                new Vela.Api.Data.Entities.EmailCode { Id = Guid.NewGuid(), Email = tag, CodeHash = "h", RequestIp = "203.0.113.1", CreatedAt = freshAt, ExpiresAt = freshAt.AddMinutes(10) });
            db.VerifyAttempts.AddRange(
                new Vela.Api.Data.Entities.VerifyAttempt { Id = Guid.NewGuid(), Email = tag, Ip = "203.0.113.1", CreatedAt = oldAt },
                new Vela.Api.Data.Entities.VerifyAttempt { Id = Guid.NewGuid(), Email = tag, Ip = "203.0.113.1", CreatedAt = freshAt });
            await db.SaveChangesAsync();
        }

        var job = w.Host.Services.GetRequiredService<AuthDataRetentionJob>();
        var (codes, attempts) = await job.RunOnceAsync(now, CancellationToken.None);

        Assert.True(codes >= 1 && attempts >= 1);
        await using var after = w.Host.Db();
        Assert.Equal(1, await after.EmailCodes.CountAsync(c => c.Email == tag));
        Assert.Equal(1, await after.VerifyAttempts.CountAsync(a => a.Email == tag));
        Assert.Equal(0, (await job.RunOnceAsync(now, CancellationToken.None)).Codes); // idempotent
        w.Dispose();
    }

    // ---- A10 / A12: text-direction control characters are refused, right-to-left text is not ----

    private const string Override = "evil\u202Egpj.exe";

    [Fact]
    public async Task Bidi_override_and_line_separator_characters_are_refused_in_every_free_text_field()
    {
        var w = await SecurityWorld.CreateAsync(pg);
        var artist = await w.NewUserAsync();
        var story = await Vela.Api.Tests.Share.ShareTestKit.Published(w.Host, artist, "Story");
        foreach (var bad in new[] { Override, "a\u2066b", "line\u2028break", "para\u2029graph" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await w.Bob.Post($"/v1/works/{story}/chapters/0/replies", new { body = bad })).Status);
            Assert.Equal(HttpStatusCode.BadRequest, (await w.Bob.Post($"/v1/artists/{artist.Id}/inquiries", new { type = "general", email = "b@example.test", message = bad })).Status);
            Assert.Equal(HttpStatusCode.BadRequest, (await artist.Patch("/v1/me/profile", new { bio = bad })).Status);
            Assert.Equal(HttpStatusCode.BadRequest, (await artist.Patch("/v1/me/profile", new { displayName = bad })).Status);
            var draft = await Vela.Api.Tests.Works.WorkTestKit.Draft(w.Host, artist);
            Assert.Equal(HttpStatusCode.BadRequest, (await artist.Put($"/v1/works/{draft}", Vela.Api.Tests.Works.WorkTestKit.Update(bad))).Status);
        }
        w.Dispose();
    }

    [Fact]
    public async Task Right_to_left_text_with_direction_marks_is_still_accepted()
    {
        var w = await SecurityWorld.CreateAsync(pg);
        var artist = await w.NewUserAsync();
        var story = await Vela.Api.Tests.Share.ShareTestKit.Published(w.Host, artist, "Story");
        foreach (var rtl in new[] { "\u0645\u0631\u062D\u0628\u0627 \u0628\u0627\u0644\u0639\u0627\u0644\u0645", "\u05E9\u05DC\u05D5\u05DD \u200F(123)\u200F", "abc \u200E\u05D0\u05D1\u05D2" })
            Assert.Equal(HttpStatusCode.Created, (await w.Bob.Post($"/v1/works/{story}/chapters/0/replies", new { body = rtl })).Status);
        w.Dispose();
    }
}

/// <summary>Smaller deferred findings that need no running host.</summary>
public class HardeningUnitTests
{
    [Fact]
    public async Task A_failed_delete_of_the_raw_avatar_upload_does_not_fail_the_avatar()
    {
        var blobs = new InMemoryBlobStore { FailDeletes = prefix => prefix.StartsWith("originals/avatars", StringComparison.Ordinal) };
        var user = Guid.NewGuid();
        blobs.Put(Vela.Api.Images.BlobNames.AvatarOriginal(user), ImageFixtures.Jpeg(300, 300));

        var name = await new Vela.Api.Images.VariantGenerator(blobs).GenerateAvatarAsync(Vela.Api.Images.BlobNames.AvatarOriginal(user), user);

        Assert.True(blobs.Exists(name));
    }

    [Theory]
    [InlineData("Originals", "works")]
    [InlineData("Originals", "avatars")]
    [InlineData("Exports", "works")]
    [InlineData("Exports", "avatars")]
    public void A_private_container_cannot_be_pointed_at_a_public_one(string which, string container)
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"Storage:Containers:{which}"] = container, ["Storage:ConnectionString"] = "UseDevelopmentStorage=true", ["Storage:PublicBaseUrl"] = "https://cdn.example.test/",
        }).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => Vela.Api.Images.StorageOptions.Load(config, new ProductionEnv()));
        Assert.Contains($"Storage:Containers:{which}", ex.Message);
    }

    private sealed class ProductionEnv : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "x";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
