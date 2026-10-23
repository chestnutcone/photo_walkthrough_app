using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Admin;
using Vela.Api.Tests.Support;
using Vela.Api.Tests.Works;
using static Vela.Api.Tests.Admin.ModerationKit;
using static Vela.Api.Tests.Legal.DmcaFlowKit;

namespace Vela.Api.Tests.Legal;

[Collection(PostgresCollection.Name)]
public class DmcaAdminPageTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private HttpClient _http = null!;
    private Browser _b = null!;
    public async Task InitializeAsync()
    {
        _s = await AdminSite.Create(pg, isolated: true);
        _http = _s.Host.Anonymous();
        _b = await _s.AdminBrowser();
    }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    private static Dictionary<string, string> F(params (string, string)[] kv) => kv.ToDictionary(x => x.Item1, x => x.Item2);
    private async Task<Guid> ReportOf(Guid work) => (await _s.DmcaReports()).Single(r => r.TargetId == work).Id;

    [Fact]
    public async Task Hostile_claimant_text_is_html_encoded_and_the_page_stays_inert()
    {
        var (_, work, _) = await _s.NoticedStory(_http, overrides: new()
        {
            ["claimantName"] = "<script>alert(1)</script>",
            ["originalWorkDescription"] = "\"><img src=x onerror=alert(2)>",
            ["claimantAddress"] = "<iframe src=//evil></iframe>",
        });

        var p = await _b.Get($"/admin/reports/{await ReportOf(work)}");

        HtmlInspector.AssertInert(p.Body);
        Assert.DoesNotContain("<script>alert", p.Body);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", p.Body);
        Assert.Contains("&lt;iframe", p.Body);
        Assert.DoesNotContain(HtmlInspector.Tags(p.Body), t => t.Name == "img" && t.Attributes.ContainsKey("onerror"));
    }

    [Fact]
    public async Task Take_down_and_reject_work_through_the_forms_with_the_antiforgery_token()
    {
        var (_, work, notice) = await _s.NoticedStory(_http);
        var (_, work2, notice2) = await _s.NoticedStory(_http);
        var r1 = await ReportOf(work);
        var page = await _b.Get($"/admin/reports/{r1}");
        Assert.Contains("Take down (DMCA)", page.Body);
        Assert.Contains("Reject notice", page.Body);

        var url = $"/admin/reports/{r1}?handler=DmcaTakedown";
        Assert.Equal(HttpStatusCode.BadRequest, (await _b.Post(url, F(("noticeId", notice.ToString())), withToken: false)).Status);
        Assert.Equal(WorkStatus.Published, (await WorkTestKit.Load(_s.Host, work)).Status);

        var ok = await _b.Post(url, F(("noticeId", notice.ToString())), tokenFrom: $"/admin/reports/{r1}");
        Assert.Contains("flash=ok", ok.Response.Headers.Location!.OriginalString);
        Assert.Equal(WorkStatus.Removed, (await WorkTestKit.Load(_s.Host, work)).Status);

        var r2 = await ReportOf(work2);
        var rej = await _b.Post($"/admin/reports/{r2}?handler=DmcaReject", F(("noticeId", notice2.ToString()), ("reason", "invalid")), tokenFrom: $"/admin/reports/{r2}");
        Assert.Contains("flash=ok", rej.Response.Headers.Location!.OriginalString);
        Assert.Equal(DmcaStatus.Rejected, (await _s.Notice(notice2)).Status);
    }

    [Fact]
    public async Task A_notice_id_from_a_different_story_is_refused()
    {
        var (_, work, _) = await _s.NoticedStory(_http);
        var (_, other, otherNotice) = await _s.NoticedStory(_http);
        var r1 = await ReportOf(work);

        var p = await _b.Post($"/admin/reports/{r1}?handler=DmcaTakedown", F(("noticeId", otherNotice.ToString())), tokenFrom: $"/admin/reports/{r1}");

        Assert.Contains("flash=not_applicable", p.Response.Headers.Location!.OriginalString);
        Assert.Equal(WorkStatus.Published, (await WorkTestKit.Load(_s.Host, other)).Status);
    }

    [Fact]
    public async Task Forward_and_restore_forms_follow_the_gating_rules()
    {
        var x = await _s.NoticedStory(_http);
        var report = await ReportOf(x.Work);
        await _b.Post($"/admin/reports/{report}?handler=DmcaTakedown", F(("noticeId", x.Notice.ToString())), tokenFrom: $"/admin/reports/{report}");
        await x.Owner.Post($"/v1/works/{x.Work}/counter-notice", Counter());
        Assert.Contains("Forward to claimant", (await _b.Get($"/admin/reports/{report}")).Body);

        var fwd = await _b.Post($"/admin/reports/{report}?handler=DmcaForward", F(("noticeId", x.Notice.ToString())), tokenFrom: $"/admin/reports/{report}");
        Assert.Contains("flash=ok", fwd.Response.Headers.Location!.OriginalString);

        var early = await _b.Post($"/admin/reports/{report}?handler=DmcaRestore", F(("noticeId", x.Notice.ToString())), tokenFrom: $"/admin/reports/{report}");
        Assert.Contains("flash=restore_too_early", early.Response.Headers.Location!.OriginalString);
        var noReason = await _b.Post($"/admin/reports/{report}?handler=DmcaRestore", F(("noticeId", x.Notice.ToString()), ("withdrew", "on")), tokenFrom: $"/admin/reports/{report}");
        Assert.Contains("flash=reason_required", noReason.Response.Headers.Location!.OriginalString);
        var ok = await _b.Post($"/admin/reports/{report}?handler=DmcaRestore", F(("noticeId", x.Notice.ToString()), ("withdrew", "on"), ("reason", "withdrawn by email")), tokenFrom: $"/admin/reports/{report}");
        Assert.Contains("flash=ok", ok.Response.Headers.Location!.OriginalString);
        Assert.Equal(WorkStatus.Published, (await WorkTestKit.Load(_s.Host, x.Work)).Status);
    }

    [Fact]
    public async Task User_reports_do_not_show_the_dmca_section_and_signed_out_visitors_cannot_post()
    {
        var (_, work) = await _s.Story();
        var report = await _s.Report(await _s.NewUser(), "work", work, "spam");
        Assert.DoesNotContain("DMCA notices for this story", (await _b.Get($"/admin/reports/{report}")).Body);

        var anon = _s.NewBrowser();
        var p = await anon.Post($"/admin/reports/{report}?handler=DmcaTakedown", F(("noticeId", Guid.NewGuid().ToString())), withToken: false);
        Assert.Equal(HttpStatusCode.Redirect, p.Status);
    }
}
