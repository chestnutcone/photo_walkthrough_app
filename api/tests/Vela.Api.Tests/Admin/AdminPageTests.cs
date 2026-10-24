using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Account;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Share;
using Vela.Api.Tests.Support;
using Vela.Api.Tests.Works;
using static Vela.Api.Tests.Admin.ModerationKit;

namespace Vela.Api.Tests.Admin;

[Collection(PostgresCollection.Name)]
public class AdminPageTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private Browser _b = null!;
    private readonly CapturingLoggerProvider _logs = new();

    public async Task InitializeAsync()
    {
        _s = await AdminSite.Create(pg, isolated: true, extra: s => s.AddLogging(l => l.AddProvider(_logs)));
        _b = await _s.AdminBrowser();
    }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    private static Dictionary<string, string> F(params (string, string)[] kv) => kv.ToDictionary(x => x.Item1, x => x.Item2);

    private async Task<Guid> SeedPendingWorkReport(string title = "Quiet harbour", string reason = "spam", string? note = null)
    {
        var (_, work) = await _s.Story(title);
        return await _s.Report(await _s.NewUser(), "work", work, reason, note);
    }

    [Fact]
    public async Task Signed_out_visitors_are_sent_to_the_login_page_from_every_admin_page()
    {
        var anon = _s.NewBrowser();
        var id = await SeedPendingWorkReport();
        foreach (var url in new[] { "/admin", $"/admin/reports/{id}", "/admin/users", $"/admin/users/{Guid.NewGuid()}", "/admin/audit" })
        {
            var p = await anon.Get(url);
            Assert.Equal(HttpStatusCode.Redirect, p.Status);
            Assert.Equal("/admin/login", p.Response.Headers.Location?.OriginalString);
        }
    }

    [Fact]
    public async Task Dashboard_shows_counts_oldest_pending_age_the_queue_oldest_first_and_refreshes_itself()
    {
        var older = await SeedPendingWorkReport("Older story");
        _s.Host.Clock.Advance(TimeSpan.FromHours(3));
        var newer = await SeedPendingWorkReport("Newer story");
        var done = await SeedPendingWorkReport("Done story");
        await _s.Mod(m => m.DismissReportAsync(_s.AdminId().Result, done));

        var p = await _b.Get("/admin");

        Assert.Equal(HttpStatusCode.OK, p.Status);
        Assert.Contains("<meta http-equiv=\"refresh\" content=\"60\">", p.Body);
        Assert.Matches(@"<b>2</b><span>Pending</span>", p.Body);
        Assert.Matches(@"<b>1</b><span>Dismissed</span>", p.Body);
        Assert.Matches(@"<b>3h 0m</b><span>Oldest pending</span>", p.Body);
        Assert.True(p.Body.IndexOf($"/admin/reports/{older}", StringComparison.Ordinal) < p.Body.IndexOf($"/admin/reports/{newer}", StringComparison.Ordinal));
        Assert.DoesNotContain($"/admin/reports/{done}", p.Body);
        // Only the dashboard refreshes itself.
        foreach (var url in new[] { $"/admin/reports/{older}", "/admin/users", "/admin/audit" })
            Assert.DoesNotContain("http-equiv", (await _b.Get(url)).Body);
    }

    [Fact]
    public async Task Queue_rows_show_the_target_reason_source_age_and_a_five_minute_thumbnail_for_work_targets()
    {
        var (_, work) = await _s.Story("Harbour");
        var reply = await _s.ReplyOn(await _s.NewUser(), work);
        await _s.Report(await _s.NewUser(), "work", work, "harassment");
        await _s.Report(await _s.NewUser(), "reply", reply, "spam");
        _s.Host.Blobs.ReadUrls.Clear();

        var p = await _b.Get("/admin");

        Assert.Contains("harassment", p.Body);
        Assert.Contains("user_report", p.Body);
        var thumbs = HtmlInspector.Tags(p.Body).Where(t => t.Name == "img").ToList();
        var thumb = Assert.Single(thumbs); // the reply row has none
        Assert.StartsWith("https://blob.test/originals/works/" + work, thumb.Attributes["src"]);
        Assert.All(_s.Host.Blobs.ReadUrls, u => Assert.True(u.TtlSeconds <= 300, "admin read links live at most five minutes"));
        Assert.NotEmpty(_s.Host.Blobs.ReadUrls);
    }

    [Fact]
    public async Task Queue_filters_by_status_reason_and_source_and_paginates_with_a_cursor()
    {
        await using (var db = _s.Host.Db())
        {
            for (var i = 0; i < 30; i++)
                db.Reports.Add(new Report { Id = Guid.NewGuid(), Source = i % 3 == 0 ? "auto_flag" : "user_report", Reason = i % 3 == 0 ? "auto:unsafe_text:6" : "spam", Status = "pending", TargetType = "user", TargetId = Guid.NewGuid(), CreatedAt = ProfileTestHost.Now.AddMinutes(-i) });
            await db.SaveChangesAsync();
        }
        var first = await _b.Get("/admin");
        Assert.Equal(25, Regex.Matches(first.Body, "href=\"/admin/reports/").Count);
        var next = Regex.Match(first.Body, "href=\"([^\"]*cursor=[^\"]+)\"").Groups[1].Value.Replace("&amp;", "&");
        var second = await _b.Get(next);
        Assert.Equal(5, Regex.Matches(second.Body, "href=\"/admin/reports/").Count);
        Assert.DoesNotContain("cursor=", second.Body);

        var auto = await _b.Get("/admin?source=auto_flag");
        Assert.Equal(10, Regex.Matches(auto.Body, "href=\"/admin/reports/").Count);
        var byReason = await _b.Get("/admin?reason=spam");
        Assert.Equal(20, Regex.Matches(byReason.Body, "href=\"/admin/reports/").Count);
        var none = await _b.Get("/admin?status=dismissed");
        Assert.Contains("Nothing here", none.Body);
        var junk = await _b.Get("/admin?status=zzz&source=zzz&cursor=zzz");
        Assert.Equal(HttpStatusCode.Redirect, junk.Status);
    }

    [Fact]
    public async Task Inline_dismiss_and_restore_change_state_and_redirect_back_after_the_post()
    {
        var report = await SeedPendingWorkReport();
        var dismiss = await _b.Post("/admin?handler=Dismiss", F(("reportId", report.ToString()), ("status", "pending")), tokenFrom: "/admin");
        Assert.Equal(HttpStatusCode.Redirect, dismiss.Status);
        Assert.StartsWith("/admin?", dismiss.Response.Headers.Location!.OriginalString);
        Assert.Contains("flash=ok", dismiss.Response.Headers.Location.OriginalString);
        Assert.Equal("dismissed", (await _s.LoadReport(report)).Status);

        var second = await SeedPendingWorkReport("Second");
        var noReason = await _b.Post("/admin?handler=Restore", F(("reportId", second.ToString()), ("actionReason", "  ")), tokenFrom: "/admin");
        Assert.Contains("flash=reason_required", noReason.Response.Headers.Location!.OriginalString);
        Assert.Equal("pending", (await _s.LoadReport(second)).Status);
        var restore = await _b.Post("/admin?handler=Restore", F(("reportId", second.ToString()), ("actionReason", "looks fine")), tokenFrom: "/admin");
        Assert.Contains("flash=ok", restore.Response.Headers.Location!.OriginalString);
        Assert.Equal("actioned", (await _s.LoadReport(second)).Status);
        var after = await _b.Get(restore.Response.Headers.Location.OriginalString);
        Assert.Contains("Done.", after.Body);
    }

    [Fact]
    public async Task Report_detail_shows_content_note_history_and_a_short_lived_private_image_that_is_never_logged()
    {
        var (owner, work) = await _s.Story("Harbour");
        var report = await _s.Report(await _s.NewUser(), "work", work, "harassment", "she is being rude");
        _s.Host.Blobs.ReadUrls.Clear();

        var p = await _b.Get($"/admin/reports/{report}");

        Assert.Equal(HttpStatusCode.OK, p.Status);
        Assert.Contains("Harbour", p.Body);
        Assert.Contains("she is being rude", p.Body);
        Assert.Contains("harassment", p.Body);
        var img = Assert.Single(HtmlInspector.Tags(p.Body), t => t.Name == "img");
        Assert.StartsWith($"https://blob.test/originals/works/{work}", img.Attributes["src"]);
        var url = Assert.Single(_s.Host.Blobs.ReadUrls);
        Assert.True(url.TtlSeconds <= 300);
        Assert.Contains($"/admin/users/{owner.Id}", p.Body);
        Assert.DoesNotContain(_logs.Lines, l => l.Contains("SECRETSIG") || l.Contains("sig=") || l.Contains("blob.test"));
        Assert.Equal(HttpStatusCode.NotFound, (await _b.Get($"/admin/reports/{Guid.NewGuid()}")).Status);
    }

    [Fact]
    public async Task Report_detail_actions_work_each_need_a_reason_and_the_ban_target_comes_from_the_report()
    {
        var (owner, work) = await _s.Story("Harbour");
        var fan = await _s.NewUser();
        var reply = await _s.ReplyOn(fan, work, "nasty words");
        var workReport = await _s.Report(await _s.NewUser(), "work", work);
        var replyReport = await _s.Report(await _s.NewUser(), "reply", reply);
        var decoy = await _s.NewUser();

        var replyPage = await _b.Get($"/admin/reports/{replyReport}");
        Assert.Contains("nasty words", replyPage.Body);
        Assert.Contains("Hide reply", replyPage.Body);
        Assert.DoesNotContain("Remove work", replyPage.Body);

        string Url(Guid id, string handler) => $"/admin/reports/{id}?handler={handler}";
        var tf = $"/admin/reports/{replyReport}";
        Assert.Contains("flash=reason_required", (await _b.Post(Url(replyReport, "HideReply"), F(("reason", "")), tokenFrom: tf)).Response.Headers.Location!.OriginalString);
        Assert.False((await _s.LoadReply(reply)).Hidden is true && (await _s.LoadReply(reply)).HiddenBy == HiddenBy.Admin);
        await _b.Post(Url(replyReport, "HideReply"), F(("reason", "rude")), tokenFrom: tf);
        Assert.Equal(HiddenBy.Admin, (await _s.LoadReply(reply)).HiddenBy);

        // A forged userId in the form is ignored: the owner of the reported content is banned.
        await _b.Post(Url(workReport, "BanOwner"), F(("reason", "repeat offender"), ("userId", decoy.Id.ToString()), ("ownerId", decoy.Id.ToString())), tokenFrom: tf);
        Assert.NotNull((await _s.LoadUser(owner.Id)).SuspendedAt);
        Assert.Null((await _s.LoadUser(decoy.Id)).SuspendedAt);

        var (_, work2) = await _s.Story("Second");
        var report2 = await _s.Report(await _s.NewUser(), "work", work2);
        await _b.Post(Url(report2, "RemoveWork"), F(("reason", "unsafe")), tokenFrom: tf);
        Assert.Equal(WorkStatus.Removed, (await WorkTestKit.Load(_s.Host, work2)).Status);
        var report3 = await SeedPendingWorkReport("Third");
        await _b.Post(Url(report3, "Restore"), F(("reason", "ok")), tokenFrom: tf);
        Assert.Equal("actioned", (await _s.LoadReport(report3)).Status);
        var report4 = await SeedPendingWorkReport("Fourth");
        await _b.Post(Url(report4, "Dismiss"), F(("reason", "")), tokenFrom: tf);
        Assert.Equal("dismissed", (await _s.LoadReport(report4)).Status);
        Assert.Equal(WorkStatus.Published, (await WorkTestKit.Load(_s.Host, work)).Status);
    }

    [Fact]
    public async Task Every_admin_post_is_rejected_without_an_antiforgery_token_and_changes_nothing()
    {
        var (owner, work) = await _s.Story("Harbour");
        var reply = await _s.ReplyOn(await _s.NewUser(), work);
        var report = await _s.Report(await _s.NewUser(), "work", work);
        var replyReport = await _s.Report(await _s.NewUser(), "reply", reply);
        var posts = new (string Url, Dictionary<string, string> Form)[]
        {
            ("/admin?handler=Dismiss", F(("reportId", report.ToString()))),
            ("/admin?handler=Restore", F(("reportId", report.ToString()), ("actionReason", "x"))),
            ($"/admin/reports/{report}?handler=Restore", F(("reason", "x"))),
            ($"/admin/reports/{report}?handler=Dismiss", F(("reason", "x"))),
            ($"/admin/reports/{report}?handler=RemoveWork", F(("reason", "x"))),
            ($"/admin/reports/{report}?handler=BanOwner", F(("reason", "x"))),
            ($"/admin/reports/{replyReport}?handler=HideReply", F(("reason", "x"))),
            ($"/admin/users/{owner.Id}?handler=Ban", F(("reason", "x"))),
            ($"/admin/users/{owner.Id}?handler=Reinstate", F(("reason", "x"))),
            ("/admin/logout", F()),
        };
        // Signing in is itself audited (admin_login), so compare against the rows that exist before the forged posts.
        var auditBefore = (await _s.Audit()).Select(a => a.Id).ToHashSet();
        Assert.Contains(await _s.Audit(), a => a.Action == "admin_login");
        foreach (var (url, form) in posts)
            Assert.Equal(HttpStatusCode.BadRequest, (await _b.Post(url, form, withToken: false)).Status);

        Assert.Equal("pending", (await _s.LoadReport(report)).Status);
        Assert.Equal(WorkStatus.Published, (await WorkTestKit.Load(_s.Host, work)).Status);
        Assert.Null((await _s.LoadUser(owner.Id)).SuspendedAt);
        Assert.Equal(auditBefore, (await _s.Audit()).Select(a => a.Id).ToHashSet()); // no new rows, none removed (no admin_logout either)
        Assert.True(await _b.IsSignedIn());
    }

    [Fact]
    public async Task User_lookup_and_detail_let_an_admin_ban_and_reinstate_without_a_report()
    {
        var target = await _s.Host.SignInOnboarded("lookup_me", "Lookup Me");
        var found = await _b.Get("/admin/users?q=lookup");
        Assert.Contains($"/admin/users/{target.Id}", found.Body);
        Assert.Contains("Lookup Me", found.Body);

        var detail = $"/admin/users/{target.Id}";
        var page = await _b.Get(detail);
        Assert.Contains("Ban account", page.Body);
        var noReason = await _b.Post(detail + "?handler=Ban", F(("reason", " ")), tokenFrom: detail);
        Assert.Contains("flash=reason_required", noReason.Response.Headers.Location!.OriginalString);
        Assert.Null((await _s.LoadUser(target.Id)).SuspendedAt);

        await _b.Post(detail + "?handler=Ban", F(("reason", "spam ring")), tokenFrom: detail);
        Assert.NotNull((await _s.LoadUser(target.Id)).SuspendedAt);
        var banned = await _b.Get(detail);
        Assert.Contains("Reinstate", banned.Body);
        Assert.Contains("spam ring", banned.Body);

        await _b.Post(detail + "?handler=Reinstate", F(("reason", "appeal")), tokenFrom: detail);
        Assert.Null((await _s.LoadUser(target.Id)).SuspendedAt);
        Assert.Equal(HttpStatusCode.NotFound, (await _b.Get($"/admin/users/{Guid.NewGuid()}")).Status);
    }

    [Fact]
    public async Task The_ban_page_refuses_self_and_other_admins_with_a_message()
    {
        var other = await _s.NewAdminUser();
        var self = await _s.AdminId();
        foreach (var (id, flash) in new[] { (self, "cannot_ban_self"), (other, "cannot_ban_admin") })
        {
            var r = await _b.Post($"/admin/users/{id}?handler=Ban", F(("reason", "x")), tokenFrom: $"/admin/users/{id}");
            Assert.Contains("flash=" + flash, r.Response.Headers.Location!.OriginalString);
            Assert.Null((await _s.LoadUser(id)).SuspendedAt);
        }
    }

    [Fact]
    public async Task Audit_page_lists_actions_newest_first()
    {
        var a = await SeedPendingWorkReport("A");
        await _b.Post($"/admin/reports/{a}?handler=Dismiss", F(("reason", "first-reason")), tokenFrom: $"/admin/reports/{a}");
        _s.Host.Clock.Advance(TimeSpan.FromMinutes(5));
        var b = await SeedPendingWorkReport("B");
        await _b.Post($"/admin/reports/{b}?handler=Dismiss", F(("reason", "second-reason")), tokenFrom: $"/admin/reports/{b}");

        var p = await _b.Get("/admin/audit");

        Assert.True(p.Body.IndexOf("second-reason", StringComparison.Ordinal) < p.Body.IndexOf("first-reason", StringComparison.Ordinal));
        Assert.Contains("dismiss", p.Body);
    }

    [Fact]
    public async Task Admin_pages_html_encode_hostile_text_and_contain_no_script_or_event_handlers()
    {
        const string evil = "\"><script>alert(1)</script><img src=x onerror=alert(2)>";
        var owner = await _s.Host.SignInOnboarded(ProfileTestHost.UniqueHandle(), evil);
        var (_, work) = await _s.Story(evil, owner);
        await WorkTestKit.Modify(_s.Host, work, w => w.Intro = evil);
        await using (var db = _s.Host.Db())
            await db.Chapters.Where(c => c.WorkId == work).ExecuteUpdateAsync(s => s.SetProperty(c => c.Body, evil).SetProperty(c => c.Title, evil));
        var reporter = await _s.NewUser();
        var reply = await _s.ReplyOn(await _s.NewUser(), work, evil);
        var report = await _s.Report(reporter, "work", work, "harassment", evil);
        var replyReport = await _s.Report(await _s.NewUser(), "reply", reply);
        await _s.Mod(m => m.HideReplyAsync(_s.AdminId().Result, reply, evil));
        await using (var db = _s.Host.Db())
            await db.Profiles.Where(p => p.UserId == owner.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.DisplayName, evil));

        foreach (var url in new[] { "/admin", $"/admin/reports/{report}", $"/admin/reports/{replyReport}", "/admin/audit", "/admin/users?q=" + Uri.EscapeDataString(owner.Id.ToString()), $"/admin/users/{owner.Id}" })
        {
            var p = await _b.Get(url);
            Assert.Equal(HttpStatusCode.OK, p.Status);
            HtmlInspector.AssertInert(p.Body);
            Assert.DoesNotContain("<script", p.Body, StringComparison.OrdinalIgnoreCase);
        }
        var detail = await _b.Get($"/admin/reports/{report}");
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", detail.Body);
        Assert.Contains("&quot;&gt;", detail.Body);
    }
}
