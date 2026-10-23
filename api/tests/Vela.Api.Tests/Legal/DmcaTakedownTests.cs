using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Admin;
using Vela.Api.Common;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Admin;
using Vela.Api.Tests.Support;
using Vela.Api.Tests.Works;
using static Vela.Api.Tests.Admin.ModerationKit;
using static Vela.Api.Tests.Legal.DmcaFlowKit;

namespace Vela.Api.Tests.Legal;

[Collection(PostgresCollection.Name)]
public class DmcaTakedownTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private HttpClient _http = null!;
    private Guid _admin;
    public async Task InitializeAsync()
    {
        _s = await AdminSite.Create(pg, isolated: true);
        _http = _s.Host.Anonymous();
        _admin = await _s.NewAdminUser();
    }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Take_down_removes_the_work_marks_the_notice_closes_the_report_audits_and_emails_the_owner()
    {
        var (owner, work, notice) = await _s.NoticedStory(_http, "<b>Bold</b> harbour");
        var ownerMail = await _s.OwnerEmail(owner.Id);

        Assert.Equal(ModerationOutcome.Ok, await _s.Dm(d => d.TakeDownAsync(_admin, notice)));

        Assert.Equal(WorkStatus.Removed, (await WorkTestKit.Load(_s.Host, work)).Status);
        var n = await _s.Notice(notice);
        Assert.Equal((DmcaStatus.Takedown, _s.Host.Clock.GetUtcNow()), (n.Status, n.TakedownAt));
        Assert.Equal("actioned", (await _s.DmcaReports()).Single().Status);
        var audit = await _s.Audit();
        Assert.Contains(audit, a => a.Action == "remove_work" && a.TargetId == work && a.Reason == $"DMCA notice #{notice}");
        Assert.Contains(audit, a => a.Action == "dmca_takedown" && a.TargetType == "dmca_notice" && a.TargetId == notice);
        var mail = Assert.Single(_s.Service<FakeEmailSender>().Sent, m => m.To == ownerMail);
        Assert.Equal("Your story was removed after a copyright notice", mail.Subject);
        Assert.Contains("<b>Bold</b> harbour", mail.TextBody);
        Assert.Contains(notice.ToString(), mail.TextBody);
        Assert.Contains("Carla Claimant", mail.TextBody);
        Assert.Contains("counter-notice", mail.TextBody);
        Assert.Contains("app", mail.TextBody);
    }

    [Fact]
    public async Task Take_down_is_refused_for_non_admins_unknown_unmatched_and_already_handled_notices()
    {
        var (_, _, notice) = await _s.NoticedStory(_http);
        var unmatched = (await _http.PostNotice(DmcaKit.Valid("nothing"))).NoticeId;
        var plain = await _s.NewUser();

        Assert.Equal(ModerationOutcome.Forbidden, await _s.Dm(d => d.TakeDownAsync(plain.Id, notice)));
        Assert.Equal(ModerationOutcome.NotFound, await _s.Dm(d => d.TakeDownAsync(_admin, Guid.NewGuid())));
        Assert.Equal(ModerationOutcome.NotApplicable, await _s.Dm(d => d.TakeDownAsync(_admin, unmatched)));
        Assert.Equal(ModerationOutcome.Ok, await _s.Dm(d => d.TakeDownAsync(_admin, notice)));
        Assert.Equal(ModerationOutcome.NotApplicable, await _s.Dm(d => d.TakeDownAsync(_admin, notice)));
        Assert.Single((await _s.Audit()), a => a.Action == "dmca_takedown");
    }

    [Fact]
    public async Task A_failing_email_does_not_undo_the_take_down()
    {
        using var site = await AdminSite.Create(pg, isolated: true, extra: s => s.AddSingleton<IEmailSender>(new ThrowingEmailSender()));
        var admin = await site.NewAdminUser();
        var (_, work, notice) = await site.NoticedStory(site.Host.Anonymous());

        Assert.Equal(ModerationOutcome.Ok, await site.Dm(d => d.TakeDownAsync(admin, notice)));
        Assert.Equal(WorkStatus.Removed, (await WorkTestKit.Load(site.Host, work)).Status);
    }

    [Fact]
    public async Task An_owner_without_a_login_email_is_still_taken_down_and_nothing_is_sent_to_them()
    {
        var (_, work) = await _s.Story();
        var notice = (await _http.PostNotice(DmcaKit.Valid(work.ToString()))).NoticeId;
        var before = _s.Service<FakeEmailSender>().Sent.Count;

        Assert.Equal(ModerationOutcome.Ok, await _s.Dm(d => d.TakeDownAsync(_admin, notice)));

        Assert.Equal(before, _s.Service<FakeEmailSender>().Sent.Count);
    }

    [Fact]
    public async Task Reject_closes_the_notice_with_an_optional_reason_and_dismisses_the_queue_item_when_nothing_else_is_pending()
    {
        var (_, work, first) = await _s.NoticedStory(_http);
        var second = (await _http.PostNotice(DmcaKit.Valid(work.ToString()))).NoticeId;

        Assert.Equal(ModerationOutcome.Ok, await _s.Dm(d => d.RejectAsync(_admin, first, "not our story")));
        Assert.Equal("pending", (await _s.DmcaReports()).Single().Status);
        Assert.Equal(ModerationOutcome.Ok, await _s.Dm(d => d.RejectAsync(_admin, second, null)));

        Assert.Equal(DmcaStatus.Rejected, (await _s.Notice(first)).Status);
        Assert.Equal("dismissed", (await _s.DmcaReports()).Single().Status);
        Assert.Equal(WorkStatus.Published, (await WorkTestKit.Load(_s.Host, work)).Status);
        var audit = (await _s.Audit()).Where(a => a.Action == "dmca_reject").ToList();
        Assert.Equal(["not our story", "rejected"], audit.Select(a => a.Reason).Order());
        Assert.Equal(ModerationOutcome.NotApplicable, await _s.Dm(d => d.RejectAsync(_admin, first, null)));
        Assert.Equal(ModerationOutcome.ReasonTooLong, await _s.Dm(d => d.RejectAsync(_admin, first, new string('x', 501))));
    }

    [Fact]
    public async Task The_repeat_infringer_count_is_the_number_of_that_users_takedown_notices()
    {
        var owner = await _s.OwnerWithEmail();
        var other = await _s.OwnerWithEmail("someone.else@owners.test");
        var notices = new List<Guid>();
        foreach (var o in new[] { owner, owner, other })
        {
            var (_, w) = await _s.Story("Story " + Guid.NewGuid().ToString("N")[..4], o);
            notices.Add((await _http.PostNotice(DmcaKit.Valid(w.ToString()))).NoticeId);
        }
        foreach (var n in notices.Take(2)) await _s.Dm(d => d.TakeDownAsync(_admin, n));
        await _s.Dm(d => d.RejectAsync(_admin, notices[2], null));

        Assert.Equal(2, await _s.Dm(d => d.TakedownCountAsync(owner.Id)));
        Assert.Equal(0, await _s.Dm(d => d.TakedownCountAsync(other.Id)));
        var b = await _s.AdminBrowser();
        Assert.Matches(@"DMCA takedowns</dt><dd>2 ", (await b.Get($"/admin/users/{owner.Id}")).Body);
    }
}
