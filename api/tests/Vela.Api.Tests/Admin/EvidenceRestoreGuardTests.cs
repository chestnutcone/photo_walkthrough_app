using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Admin;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Legal;
using Vela.Api.Tests.Support;
using Vela.Api.Tests.Works;
using static Vela.Api.Tests.Admin.ModerationKit;
using static Vela.Api.Tests.Legal.DmcaFlowKit;

namespace Vela.Api.Tests.Admin;

[Collection(PostgresCollection.Name)]
public class EvidenceRestoreGuardTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private Guid _admin;
    public async Task InitializeAsync() { _s = await AdminSite.Create(pg, isolated: true); _admin = await _s.NewAdminUser(); }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Held_works_cannot_be_restored_through_the_report_restore()
    {
        var (_, work) = await _s.Story();
        var report = await _s.Report(await _s.NewUser(), "work", work, "illegal_content");
        await _s.MarkIllegal(_admin, report);

        Assert.Equal(ModerationOutcome.EvidenceHeld, await _s.Mod(m => m.RestoreAsync(_admin, report, "mistake")));

        Assert.Equal(WorkStatus.Removed, (await WorkTestKit.Load(_s.Host, work)).Status);
    }

    [Fact]
    public async Task Held_replies_cannot_be_restored()
    {
        var (_, work) = await _s.Story();
        var reply = await _s.ReplyOn(await _s.NewUser(), work);
        var report = await _s.Report(await _s.NewUser(), "reply", reply, "illegal_content");
        await _s.MarkIllegal(_admin, report);

        Assert.Equal(ModerationOutcome.EvidenceHeld, await _s.Mod(m => m.RestoreAsync(_admin, report, "mistake")));

        Assert.True((await _s.LoadReply(reply)).Hidden);
    }

    [Fact]
    public async Task Reinstating_an_owner_with_a_hold_is_refused_for_every_target_type()
    {
        var (owner, work) = await _s.Story();
        await _s.MarkIllegal(_admin, await _s.Report(await _s.NewUser(), "work", work, "illegal_content"));
        var target = await _s.NewUser();
        await _s.MarkIllegal(_admin, await _s.Report(await _s.NewUser(), "user", target.Id, "illegal_content"));

        Assert.Equal(ModerationOutcome.EvidenceHeld, await _s.Mod(m => m.ReinstateUserAsync(_admin, owner.Id, "sorry")));
        Assert.Equal(ModerationOutcome.EvidenceHeld, await _s.Mod(m => m.ReinstateUserAsync(_admin, target.Id, "sorry")));

        Assert.NotNull((await _s.LoadUser(owner.Id)).SuspendedAt);
        Assert.NotNull((await _s.LoadUser(target.Id)).SuspendedAt);
    }

    [Fact]
    public async Task The_dmca_restore_is_refused_for_held_works_even_after_ten_days_or_when_withdrawn()
    {
        var http = _s.Host.Anonymous();
        var x = await _s.NoticedStory(http);
        await _s.Dm(d => d.TakeDownAsync(_admin, x.Notice));
        await x.Owner.Post($"/v1/works/{x.Work}/counter-notice", Counter());
        await _s.Dm(d => d.ForwardAsync(_admin, x.Notice));
        await _s.MarkIllegal(_admin, (await _s.DmcaReports()).Single().Id);
        _s.Host.Clock.Advance(TimeSpan.FromDays(11));

        Assert.Equal(ModerationOutcome.EvidenceHeld, await _s.Dm(d => d.RestoreAsync(_admin, x.Notice, false, null)));
        Assert.Equal(ModerationOutcome.EvidenceHeld, await _s.Dm(d => d.RestoreAsync(_admin, x.Notice, true, "withdrawn")));

        Assert.Equal(WorkStatus.Removed, (await WorkTestKit.Load(_s.Host, x.Work)).Status);
        Assert.Equal(DmcaStatus.Forwarded, (await _s.Notice(x.Notice)).Status);
    }

    [Fact]
    public async Task Pages_do_not_offer_restore_or_reinstate_for_held_content()
    {
        var (owner, work) = await _s.Story();
        var held = await _s.Report(await _s.NewUser(), "work", work, "illegal_content");
        await _s.MarkIllegal(_admin, held);
        var (_, other) = await _s.Story();
        var plain = await _s.Report(await _s.NewUser(), "work", other, "spam");
        var b = await _s.AdminBrowser();

        Assert.DoesNotContain("handler=Restore", (await b.Get($"/admin/reports/{held}")).Body);
        Assert.Contains("handler=Restore", (await b.Get($"/admin/reports/{plain}")).Body);
        Assert.DoesNotContain("handler=Reinstate", (await b.Get($"/admin/users/{owner.Id}")).Body);
        var post = await b.Post($"/admin/reports/{held}?handler=Restore", new Dictionary<string, string> { ["reason"] = "x" }, tokenFrom: $"/admin/reports/{held}");
        Assert.Contains("flash=evidence_held", post.Response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Typing_the_dmca_restore_reason_into_the_generic_form_does_not_skip_the_ten_day_gate()
    {
        var http = _s.Host.Anonymous();
        var x = await _s.NoticedStory(http);
        await _s.Dm(d => d.TakeDownAsync(_admin, x.Notice));
        await x.Owner.Post($"/v1/works/{x.Work}/counter-notice", Counter());
        await _s.Dm(d => d.ForwardAsync(_admin, x.Notice));
        var report = (await _s.DmcaReports()).Single().Id;
        var b = await _s.AdminBrowser();

        var p = await b.Post($"/admin/reports/{report}?handler=Restore", new Dictionary<string, string> { ["reason"] = "DMCA counter-notice" }, tokenFrom: $"/admin/reports/{report}");

        Assert.Contains("flash=dmca_process", p.Response.Headers.Location!.OriginalString);
        Assert.Equal(WorkStatus.Removed, (await WorkTestKit.Load(_s.Host, x.Work)).Status);
        Assert.Equal(ModerationOutcome.DmcaProcessRequired, await _s.Mod(m => m.RestoreAsync(_admin, report, " DMCA counter-notice ")));
    }
}
