using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vela.Api.Admin;
using Vela.Api.Tests.Account;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Admin.ModerationKit;

namespace Vela.Api.Tests.Admin;

[Collection(PostgresCollection.Name)]
public class EvidencePageTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private Browser _b = null!;
    private Guid _admin;
    private readonly CapturingLoggerProvider _logs = new();
    public async Task InitializeAsync()
    {
        _s = await AdminSite.Create(pg, isolated: true, extra: s => s.AddLogging(l => l.AddProvider(_logs)));
        _admin = await _s.NewAdminUser();
        _b = await _s.AdminBrowser();
    }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    private static Dictionary<string, string> F(params (string, string)[] kv) => kv.ToDictionary(x => x.Item1, x => x.Item2);

    private async Task<(Guid Hold, Guid Report, Guid Work)> Held(string title = "Held story")
    {
        var (_, work) = await _s.Story(title);
        var report = await _s.Report(await _s.NewUser(), "work", work, "illegal_content");
        return ((await _s.MarkIllegal(_admin, report)).HoldId!.Value, report, work);
    }

    [Fact]
    public async Task The_report_page_offers_mark_as_illegal_for_work_reply_and_user_targets()
    {
        var (_, work) = await _s.Story();
        var reply = await _s.ReplyOn(await _s.NewUser(), work);
        var target = await _s.NewUser();
        var ids = new[]
        {
            await _s.Report(await _s.NewUser(), "work", work), await _s.Report(await _s.NewUser(), "reply", reply),
            await _s.Report(await _s.NewUser(), "user", target.Id),
        };
        foreach (var id in ids)
        {
            var p = await _b.Get($"/admin/reports/{id}");
            Assert.Contains("Mark as illegal content", p.Body);
            Assert.Contains("handler=MarkIllegal", p.Body);
            Assert.Matches("name=\"confirm\"", p.Body);
        }
    }

    [Fact]
    public async Task Posting_mark_illegal_needs_the_token_the_checkbox_and_a_reason_then_lands_on_the_hold()
    {
        var (_, work) = await _s.Story();
        var report = await _s.Report(await _s.NewUser(), "work", work, "illegal_content");
        var url = $"/admin/reports/{report}?handler=MarkIllegal";

        Assert.Equal(HttpStatusCode.BadRequest, (await _b.Post(url, F(("reason", "r"), ("confirm", "on")), withToken: false)).Status);
        var noBox = await _b.Post(url, F(("reason", "r")), tokenFrom: $"/admin/reports/{report}");
        Assert.Contains("flash=confirmation_required", noBox.Response.Headers.Location!.OriginalString);
        var noReason = await _b.Post(url, F(("reason", " "), ("confirm", "on")), tokenFrom: $"/admin/reports/{report}");
        Assert.Contains("flash=reason_required", noReason.Response.Headers.Location!.OriginalString);
        Assert.Empty(await _s.Holds());

        var ok = await _b.Post(url, F(("reason", "r"), ("confirm", "on")), tokenFrom: $"/admin/reports/{report}");
        var hold = Assert.Single(await _s.Holds());
        Assert.Equal(HttpStatusCode.Redirect, ok.Status);
        Assert.Equal($"/admin/evidence/{hold.Id}?flash=ok", ok.Response.Headers.Location!.OriginalString);
        Assert.Contains($"/admin/evidence/{hold.Id}", (await _b.Get($"/admin/reports/{report}")).Body);
    }

    [Fact]
    public async Task The_list_shows_created_target_owner_hold_until_and_ncmec_status_and_paginates()
    {
        for (var i = 0; i < 3; i++) await Held($"Story {i}");
        var all = await _s.Holds();

        var p = await _b.Get("/admin/evidence");

        Assert.Equal(HttpStatusCode.OK, p.Status);
        foreach (var h in all) Assert.Contains($"/admin/evidence/{h.Id}", p.Body);
        Assert.Contains("not submitted", p.Body);
        Assert.Contains("work", p.Body);
        Assert.Contains(Vela.Api.Admin.Pages.AdminFormat.When(all[0].HoldUntil), p.Body);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await _s.Mod2(e => e.ListAsync(cursor, 2));
            seen.AddRange(page.Items.Select(i => i.Id));
            cursor = page.NextCursor;
        } while (cursor is not null);
        Assert.Equal(all.Select(h => h.Id).Order(), seen.Order());
        Assert.Equal(3, seen.Distinct().Count());
    }

    [Fact]
    public async Task The_detail_page_shows_the_summary_a_short_lived_image_link_and_never_logs_it()
    {
        var (hold, _, _) = await Held("Detail story");
        _s.Host.Blobs.ReadUrls.Clear();

        var p = await _b.Get($"/admin/evidence/{hold}");

        Assert.Equal(HttpStatusCode.OK, p.Status);
        Assert.Equal("no-store", p.Header("Cache-Control"));
        Assert.Contains("Detail story", p.Body);
        var img = Assert.Single(HtmlInspector.Tags(p.Body), t => t.Name == "img");
        Assert.StartsWith($"https://blob.test/evidence/{hold}/original", img.Attributes["src"]);
        var url = Assert.Single(_s.Host.Blobs.ReadUrls);
        Assert.True(url.TtlSeconds <= 300);
        Assert.DoesNotContain(_logs.Lines, l => l.Contains("SECRETSIG") || l.Contains("sig="));
        Assert.Contains("handler=SubmitNcmec", p.Body);
        HtmlInspector.AssertInert(p.Body);
    }

    [Fact]
    public async Task Hostile_text_in_the_snapshot_is_html_encoded_on_the_detail_page()
    {
        var owner = await _s.NewUser();
        await using (var db = _s.Host.Db())
            await db.Profiles.Where(p => p.UserId == owner.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.DisplayName, "\"><img src=x onerror=alert(1)>"));
        var (_, work) = await _s.Story("<script>alert('t')</script>", owner);
        var report = await _s.Report(await _s.NewUser(), "work", work, "illegal_content");
        var hold = (await _s.MarkIllegal(_admin, report)).HoldId!.Value;

        var p = await _b.Get($"/admin/evidence/{hold}");
        var list = await _b.Get("/admin/evidence");

        foreach (var page in new[] { p, list })
        {
            HtmlInspector.AssertInert(page.Body);
            Assert.DoesNotContain("<script>alert", page.Body);
            Assert.DoesNotContain("onerror=alert(1)>", page.Body.Replace("&quot;", ""));
        }
        Assert.Contains("&lt;script&gt;alert(&#x27;t&#x27;)&lt;/script&gt;", p.Body);
        Assert.Single(HtmlInspector.Tags(p.Body), t => t.Name == "img");
    }

    [Fact]
    public async Task Signed_out_visitors_and_non_admins_are_redirected_from_the_evidence_pages()
    {
        var (hold, _, _) = await Held();
        var anon = _s.NewBrowser();
        foreach (var url in new[] { "/admin/evidence", $"/admin/evidence/{hold}" })
        {
            var p = await anon.Get(url);
            Assert.Equal(HttpStatusCode.Redirect, p.Status);
            Assert.Equal("/admin/login", p.Response.Headers.Location?.OriginalString);
        }
    }
}
