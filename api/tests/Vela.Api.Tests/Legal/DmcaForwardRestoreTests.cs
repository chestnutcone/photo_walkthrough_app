using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Admin;
using Vela.Api.Common;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Admin;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using Vela.Api.Tests.Works;
using static Vela.Api.Tests.Admin.ModerationKit;
using static Vela.Api.Tests.Legal.DmcaFlowKit;

namespace Vela.Api.Tests.Legal;

[Collection(PostgresCollection.Name)]
public class DmcaForwardRestoreTests(PostgresFixture pg) : IAsyncLifetime
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

    private async Task<(TestUser Owner, Guid Work, Guid Notice)> Countered()
    {
        var x = await _s.NoticedStory(_http);
        await _s.Dm(d => d.TakeDownAsync(_admin, x.Notice));
        Assert.Equal(HttpStatusCode.OK, (await x.Owner.Post($"/v1/works/{x.Work}/counter-notice", Counter())).Status);
        return x;
    }

    [Fact]
    public async Task Forward_emails_the_claimant_the_counter_notice_and_starts_the_waiting_period()
    {
        var (_, _, notice) = await Countered();

        Assert.Equal(ModerationOutcome.Ok, await _s.Dm(d => d.ForwardAsync(_admin, notice)));

        var n = await _s.Notice(notice);
        Assert.Equal((DmcaStatus.Forwarded, _s.Host.Clock.GetUtcNow()), (n.Status, n.ForwardedAt));
        var mail = Assert.Single(_s.Service<FakeEmailSender>().Sent, m => m.Subject == "Counter-notice received");
        Assert.Equal("carla@claims.test", mail.To);
        foreach (var part in new[] { "Olive Owner", "9 Side Road\nShelbyville", "555-0199", "I own this photograph.", notice.ToString() })
            Assert.Contains(part, mail.TextBody);
        Assert.Contains(await _s.Audit(), a => a.Action == "dmca_forward" && a.TargetId == notice);
        Assert.Equal(ModerationOutcome.NotApplicable, await _s.Dm(d => d.ForwardAsync(_admin, notice)));
    }

    [Fact]
    public async Task Forward_only_applies_to_counter_notices_and_only_for_admins()
    {
        var x = await _s.NoticedStory(_http);
        Assert.Equal(ModerationOutcome.NotApplicable, await _s.Dm(d => d.ForwardAsync(_admin, x.Notice)));
        Assert.Equal(ModerationOutcome.NotFound, await _s.Dm(d => d.ForwardAsync(_admin, Guid.NewGuid())));
        Assert.Equal(ModerationOutcome.Forbidden, await _s.Dm(d => d.ForwardAsync(x.Owner.Id, x.Notice)));
    }

    [Fact]
    public async Task A_failed_forward_email_records_nothing_so_it_can_be_retried()
    {
        var toggle = new FlakySender();
        using var site = await AdminSite.Create(pg, isolated: true, extra: s => s.AddSingleton<IEmailSender>(toggle));
        var admin = await site.NewAdminUser();
        var http = site.Host.Anonymous();
        var x = await site.NoticedStory(http);
        await site.Dm(d => d.TakeDownAsync(admin, x.Notice));
        await x.Owner.Post($"/v1/works/{x.Work}/counter-notice", Counter());

        toggle.Fail = true;
        Assert.Equal(ModerationOutcome.DeliveryFailed, await site.Dm(d => d.ForwardAsync(admin, x.Notice)));
        Assert.Equal(DmcaStatus.CounterNotice, (await site.Notice(x.Notice)).Status);
        toggle.Fail = false;
        Assert.Equal(ModerationOutcome.Ok, await site.Dm(d => d.ForwardAsync(admin, x.Notice)));
        Assert.NotNull((await site.Notice(x.Notice)).ForwardedAt);
    }

    [Fact]
    public async Task Parallel_forwards_email_the_claimant_once()
    {
        var (_, _, notice) = await Countered();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _s.Dm(d => d.ForwardAsync(_admin, notice))));

        Assert.Equal(1, results.Count(r => r == ModerationOutcome.Ok));
        Assert.Single(_s.Service<FakeEmailSender>().Sent, m => m.Subject == "Counter-notice received");
    }

    [Fact]
    public async Task The_claimant_is_emailed_only_after_the_forward_is_recorded_and_a_failed_send_is_rolled_back()
    {
        var seen = new List<string>();
        var sender = new FlakySender();
        using var site = await AdminSite.Create(pg, isolated: true, extra: s => s.AddSingleton<IEmailSender>(sender));
        var admin = await site.NewAdminUser();
        var x = await site.NoticedStory(site.Host.Anonymous());
        await site.Dm(d => d.TakeDownAsync(admin, x.Notice));
        await x.Owner.Post($"/v1/works/{x.Work}/counter-notice", Counter());
        sender.OnSend = async () => seen.Add((await site.Notice(x.Notice)).Status);

        Assert.Equal(ModerationOutcome.Ok, await site.Dm(d => d.ForwardAsync(admin, x.Notice)));

        Assert.Equal([DmcaStatus.Forwarded], seen);
        var n = await site.Notice(x.Notice);
        Assert.Equal(site.Host.Clock.GetUtcNow(), n.ForwardedAt);
    }

    private sealed class FlakySender : IEmailSender
    {
        public bool Fail;
        public Func<Task>? OnSend;
        public async Task SendAsync(string to, string subject, string textBody)
        {
            if (OnSend is { } hook) await hook();
            if (Fail) throw new IOException("down");
        }
    }

    [Fact]
    public async Task Restore_waits_ten_days_from_forwarding_to_the_second()
    {
        var (_, work, notice) = await Countered();
        await _s.Dm(d => d.ForwardAsync(_admin, notice));
        var forwarded = _s.Host.Clock.GetUtcNow();

        _s.Host.Clock.Advance(TimeSpan.FromDays(10) - TimeSpan.FromSeconds(1));
        Assert.Equal(ModerationOutcome.RestoreTooEarly, await _s.Dm(d => d.RestoreAsync(_admin, notice, false, null)));
        Assert.Equal(WorkStatus.Removed, (await WorkTestKit.Load(_s.Host, work)).Status);
        Assert.Equal(DmcaStatus.Forwarded, (await _s.Notice(notice)).Status);

        _s.Host.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(forwarded + TimeSpan.FromDays(10), _s.Host.Clock.GetUtcNow());
        Assert.Equal(ModerationOutcome.Ok, await _s.Dm(d => d.RestoreAsync(_admin, notice, false, null)));

        Assert.Equal(WorkStatus.Published, (await WorkTestKit.Load(_s.Host, work)).Status);
        Assert.Equal(DmcaStatus.Restored, (await _s.Notice(notice)).Status);
        var audit = await _s.Audit();
        Assert.Contains(audit, a => a.Action == "restore" && a.TargetId == work && a.Reason == "DMCA counter-notice");
        Assert.Contains(audit, a => a.Action == "dmca_restore" && a.TargetId == notice);
        Assert.Equal(ModerationOutcome.NotApplicable, await _s.Dm(d => d.RestoreAsync(_admin, notice, false, null)));
    }

    [Fact]
    public async Task The_claimant_withdrew_override_restores_early_but_needs_a_reason()
    {
        var (_, work, notice) = await Countered();
        await _s.Dm(d => d.ForwardAsync(_admin, notice));

        Assert.Equal(ModerationOutcome.ReasonRequired, await _s.Dm(d => d.RestoreAsync(_admin, notice, true, "  ")));
        Assert.Equal(ModerationOutcome.ReasonTooLong, await _s.Dm(d => d.RestoreAsync(_admin, notice, true, new string('x', 501))));
        Assert.Equal(WorkStatus.Removed, (await WorkTestKit.Load(_s.Host, work)).Status);

        Assert.Equal(ModerationOutcome.Ok, await _s.Dm(d => d.RestoreAsync(_admin, notice, true, "email from claimant")));
        Assert.Equal(WorkStatus.Published, (await WorkTestKit.Load(_s.Host, work)).Status);
        Assert.Contains(await _s.Audit(), a => a.Action == "dmca_restore" && a.Reason == "claimant withdrew: email from claimant");
    }

    [Fact]
    public async Task Restore_is_only_possible_after_forwarding_and_never_through_the_generic_restore()
    {
        var (_, work, notice) = await Countered();
        var report = (await _s.DmcaReports()).Single().Id;
        _s.Host.Clock.Advance(TimeSpan.FromDays(30));

        Assert.Equal(ModerationOutcome.NotApplicable, await _s.Dm(d => d.RestoreAsync(_admin, notice, true, "r")));
        Assert.Equal(ModerationOutcome.DmcaProcessRequired, await _s.Mod(m => m.RestoreAsync(_admin, report, "I think it is fine")));
        Assert.Equal(WorkStatus.Removed, (await WorkTestKit.Load(_s.Host, work)).Status);
    }

    [Fact]
    public async Task The_report_page_shows_the_notice_the_counter_notice_and_the_earliest_restore_date()
    {
        var (_, work, notice) = await Countered();
        await _s.Dm(d => d.ForwardAsync(_admin, notice));
        var report = (await _s.DmcaReports()).Single().Id;
        var b = await _s.AdminBrowser();

        var p = await b.Get($"/admin/reports/{report}");

        Assert.Contains("Carla Claimant", p.Body);
        Assert.Contains("I own this photograph.", p.Body);
        Assert.Contains("Earliest restore", p.Body);
        Assert.Contains(Vela.Api.Admin.Pages.AdminFormat.When(_s.Host.Clock.GetUtcNow().AddDays(10)), p.Body);
        Assert.Contains("Restore work", p.Body);
        Assert.Contains("name=\"withdrew\"", p.Body);
        Assert.DoesNotContain("Take down (DMCA)", p.Body);
        _ = work;
    }
}
