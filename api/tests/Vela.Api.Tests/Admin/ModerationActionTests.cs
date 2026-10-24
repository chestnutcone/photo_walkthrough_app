using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Admin;
using Vela.Api.Common;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using Vela.Api.Tests.Works;
using static Vela.Api.Tests.Admin.ModerationKit;

namespace Vela.Api.Tests.Admin;

[Collection(PostgresCollection.Name)]
public class ModerationActionTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private Guid _admin;
    public async Task InitializeAsync()
    {
        _s = await AdminSite.Create(pg, isolated: true, extra: s => s.AddScoped<IDomainEventHandler<WorkPublished>, RecordingHandler>());
        _admin = await _s.NewAdminUser();
    }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    private static readonly string Long = new('x', 501);

    public static IEnumerable<object[]> BadReasons() => [[""], ["   "], [Long]];

    [Theory]
    [MemberData(nameof(BadReasons))]
    public async Task Every_action_needs_a_reason_of_one_to_500_characters_and_a_refused_action_changes_nothing(string reason)
    {
        var (owner, work) = await _s.Story();
        var fan = await _s.NewUser();
        var reply = await _s.ReplyOn(fan, work);
        var report = await _s.Report(fan, "work", work);
        var expected = reason.Length > 500 ? ModerationOutcome.ReasonTooLong : ModerationOutcome.ReasonRequired;

        Assert.Equal(expected, await _s.Mod(m => m.RemoveWorkAsync(_admin, work, reason)));
        Assert.Equal(expected, await _s.Mod(m => m.HideReplyAsync(_admin, reply, reason)));
        Assert.Equal(expected, await _s.Mod(m => m.RestoreAsync(_admin, report, reason)));
        Assert.Equal(expected, await _s.Mod(m => m.SuspendUserAsync(_admin, owner.Id, reason)));
        Assert.Equal(expected, await _s.Mod(m => m.ReinstateUserAsync(_admin, owner.Id, reason)));

        Assert.Equal(WorkStatus.Published, (await WorkTestKit.Load(_s.Host, work)).Status);
        Assert.False((await _s.LoadReply(reply)).Hidden);
        Assert.Null((await _s.LoadUser(owner.Id)).SuspendedAt);
        Assert.Equal("pending", (await _s.LoadReport(report)).Status);
        Assert.Empty(await _s.Audit());
    }

    [Fact]
    public async Task A_reason_of_exactly_500_characters_is_accepted_and_trimmed_into_the_audit_row()
    {
        var (_, work) = await _s.Story();
        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.RemoveWorkAsync(_admin, work, "  " + new string('r', 500) + "  ")));
        Assert.Equal(new string('r', 500), (await _s.Audit()).Single().Reason);
    }

    [Fact]
    public async Task Only_a_live_admin_may_act()
    {
        var (_, work) = await _s.Story();
        var plain = await _s.NewUser();
        Assert.Equal(ModerationOutcome.Forbidden, await _s.Mod(m => m.RemoveWorkAsync(plain.Id, work, "why")));
        Assert.Equal(ModerationOutcome.Forbidden, await _s.Mod(m => m.RemoveWorkAsync(Guid.NewGuid(), work, "why")));
        await using (var db = _s.Host.Db())
            await db.Users.Where(u => u.Id == _admin).ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, ProfileTestHost.Now));
        Assert.Equal(ModerationOutcome.Forbidden, await _s.Mod(m => m.RemoveWorkAsync(_admin, work, "why")));
        Assert.Empty(await _s.Audit());
    }

    [Fact]
    public async Task Missing_targets_give_not_found_and_leave_no_audit_row()
    {
        var ghost = Guid.NewGuid();
        Assert.Equal(ModerationOutcome.NotFound, await _s.Mod(m => m.RemoveWorkAsync(_admin, ghost, "why")));
        Assert.Equal(ModerationOutcome.NotFound, await _s.Mod(m => m.HideReplyAsync(_admin, ghost, "why")));
        Assert.Equal(ModerationOutcome.NotFound, await _s.Mod(m => m.RestoreAsync(_admin, ghost, "why")));
        Assert.Equal(ModerationOutcome.NotFound, await _s.Mod(m => m.DismissReportAsync(_admin, ghost)));
        Assert.Equal(ModerationOutcome.NotFound, await _s.Mod(m => m.SuspendUserAsync(_admin, ghost, "why")));
        Assert.Equal(ModerationOutcome.NotFound, await _s.Mod(m => m.ReinstateUserAsync(_admin, ghost, "why")));
        Assert.Empty(await _s.Audit());
    }

    [Fact]
    public async Task Admins_cannot_ban_themselves_or_another_admin()
    {
        var other = await _s.NewAdminUser();
        Assert.Equal(ModerationOutcome.CannotBanSelf, await _s.Mod(m => m.SuspendUserAsync(_admin, _admin, "oops")));
        Assert.Equal(ModerationOutcome.CannotBanAdmin, await _s.Mod(m => m.SuspendUserAsync(_admin, other, "coup")));
        Assert.Null((await _s.LoadUser(other)).SuspendedAt);
        Assert.Null((await _s.LoadUser(_admin)).SuspendedAt);
        Assert.Empty(await _s.Audit());
    }

    [Fact]
    public async Task Removing_a_work_sets_removed_and_resolves_every_pending_report_on_it_together()
    {
        var (_, work) = await _s.Story();
        var a = await _s.NewUser(); var b = await _s.NewUser();
        var r1 = await _s.Report(a, "work", work); var r2 = await _s.Report(b, "work", work, "harassment");
        var elsewhere = await _s.Report(a, "user", (await _s.NewUser()).Id);

        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.RemoveWorkAsync(_admin, work, "not allowed here")));

        Assert.Equal(WorkStatus.Removed, (await WorkTestKit.Load(_s.Host, work)).Status);
        Assert.Equal("actioned", (await _s.LoadReport(r1)).Status);
        Assert.Equal("actioned", (await _s.LoadReport(r2)).Status);
        Assert.Equal("pending", (await _s.LoadReport(elsewhere)).Status);
    }

    [Fact]
    public async Task Confirming_an_auto_block_removes_the_work_and_closes_the_auto_flag()
    {
        var (_, work) = await _s.BlockedStory();
        var flag = await _s.AutoFlagId("work", work);
        Assert.Equal(WorkStatus.Blocked, (await WorkTestKit.Load(_s.Host, work)).Status);

        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.RemoveWorkAsync(_admin, work, "confirmed unsafe")));

        Assert.Equal(WorkStatus.Removed, (await WorkTestKit.Load(_s.Host, work)).Status);
        Assert.Equal("actioned", (await _s.LoadReport(flag)).Status);
    }

    [Fact]
    public async Task Hiding_a_reply_marks_it_admin_hidden_and_resolves_its_reports()
    {
        var (_, work) = await _s.Story();
        var fan = await _s.NewUser(); var reporter = await _s.NewUser();
        var reply = await _s.ReplyOn(fan, work);
        var report = await _s.Report(reporter, "reply", reply);

        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.HideReplyAsync(_admin, reply, "abuse")));

        var row = await _s.LoadReply(reply);
        Assert.True(row.Hidden);
        Assert.Equal(HiddenBy.Admin, row.HiddenBy);
        Assert.Equal("actioned", (await _s.LoadReport(report)).Status);
    }

    [Fact]
    public async Task Restoring_an_auto_blocked_work_republishes_it_with_override_variants_and_one_event()
    {
        var (_, work) = await _s.BlockedStory();
        var flag = await _s.AutoFlagId("work", work);
        Assert.All(Variants(work), v => Assert.False(_s.Host.Blobs.Exists(v)));
        RecordingHandler.Seen.Clear();

        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.RestoreAsync(_admin, flag, "false positive")));

        var w = await WorkTestKit.Load(_s.Host, work);
        Assert.Equal(WorkStatus.Published, w.Status);
        Assert.True(w.ModerationOverride);
        Assert.NotNull(w.PublishedAt);
        Assert.False(w.ReportHidden);
        Assert.True(w.Width > 0 && w.Height > 0);
        Assert.All(Variants(work), v => Assert.True(_s.Host.Blobs.Exists(v), v));
        Assert.Single(RecordingHandler.Seen, e => e.WorkId == work);
        Assert.Equal("actioned", (await _s.LoadReport(flag)).Status);
        // A restored work is never auto-blocked again.
        await WorkTestKit.Modify(_s.Host, work, x => { x.Status = WorkStatus.Pending; x.PendingSince = ProfileTestHost.Now; });
        await _s.RunPipeline();
        Assert.Equal(WorkStatus.Published, (await WorkTestKit.Load(_s.Host, work)).Status);
    }

    [Fact]
    public async Task Restoring_a_removed_work_publishes_it_and_followers_get_no_second_new_story_notification()
    {
        var owner = await _s.NewUser();
        var follower = await _s.NewUser();
        Assert.Equal(System.Net.HttpStatusCode.OK, (await follower.Put($"/v1/artists/{owner.Id}/follow")).Status);
        var (_, work) = await _s.Story("Harbour", owner);
        var flag = await _s.Report(follower, "work", work);
        await using (var db = _s.Host.Db())
            Assert.Equal(1, await db.Notifications.CountAsync(n => n.UserId == follower.Id && n.Type == "NewStory" && n.WorkId == work));

        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.RemoveWorkAsync(_admin, work, "mistake")));
        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.RestoreAsync(_admin, flag, "reverted")));

        Assert.Equal(WorkStatus.Published, (await WorkTestKit.Load(_s.Host, work)).Status);
        await using var db2 = _s.Host.Db();
        Assert.Equal(1, await db2.Notifications.CountAsync(n => n.UserId == follower.Id && n.Type == "NewStory" && n.WorkId == work));
    }

    [Fact]
    public async Task Restore_unhides_auto_hidden_report_hidden_and_admin_hidden_replies()
    {
        var (_, work) = await _s.Story();
        var fan = await _s.NewUser(); var reporter = await _s.NewUser();
        var auto = await _s.ReplyOn(fan, work, "bad " + Unsafe);
        var autoFlag = await _s.AutoFlagId("reply", auto);
        Assert.Equal(HiddenBy.Auto, (await _s.LoadReply(auto)).HiddenBy);
        var reported = await _s.ReplyOn(fan, work, "meh");
        var reportRow = await _s.Report(reporter, "reply", reported);
        Assert.Equal(HiddenBy.Report, (await _s.LoadReply(reported)).HiddenBy);
        var byAdmin = await _s.ReplyOn(fan, work, "rude");
        var third = await _s.NewUser();
        var adminRow = await _s.Report(third, "reply", byAdmin);
        await _s.Mod(m => m.HideReplyAsync(_admin, byAdmin, "rude"));

        foreach (var (report, reply) in new[] { (autoFlag, auto), (reportRow, reported), (adminRow, byAdmin) })
        {
            Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.RestoreAsync(_admin, report, "fine on review")));
            var r = await _s.LoadReply(reply);
            Assert.False(r.Hidden);
            Assert.Null(r.HiddenBy);
        }
    }

    [Fact]
    public async Task Restoring_a_report_hidden_work_lifts_the_hide_without_republishing()
    {
        var (_, work) = await _s.Story();
        var report = await _s.Report(await _s.NewUser(), "work", work);
        Assert.True((await WorkTestKit.Load(_s.Host, work)).ReportHidden);
        RecordingHandler.Seen.Clear();

        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.RestoreAsync(_admin, report, "fine")));

        var w = await WorkTestKit.Load(_s.Host, work);
        Assert.False(w.ReportHidden);
        Assert.Equal(WorkStatus.Published, w.Status);
        Assert.Empty(RecordingHandler.Seen);
    }

    [Fact]
    public async Task Restore_does_not_apply_to_a_user_report_and_fails_cleanly_when_the_original_is_gone()
    {
        var target = await _s.NewUser();
        var userReport = await _s.Report(await _s.NewUser(), "user", target.Id);
        Assert.Equal(ModerationOutcome.NotApplicable, await _s.Mod(m => m.RestoreAsync(_admin, userReport, "x")));

        var (_, work) = await _s.BlockedStory();
        var flag = await _s.AutoFlagId("work", work);
        await _s.Host.Blobs.DeletePrefixAsync(await WorkTestKit.OriginalOf(_s.Host, work));
        Assert.Equal(ModerationOutcome.VariantsUnavailable, await _s.Mod(m => m.RestoreAsync(_admin, flag, "x")));
        Assert.Equal(WorkStatus.Blocked, (await WorkTestKit.Load(_s.Host, work)).Status);
        Assert.Equal("pending", (await _s.LoadReport(flag)).Status);
    }

    [Fact]
    public async Task Dismissing_resolves_all_pending_reports_on_the_target_and_unhides_report_hidden_content()
    {
        var (_, work) = await _s.Story();
        var a = await _s.NewUser(); var b = await _s.NewUser();
        var r1 = await _s.Report(a, "work", work); var r2 = await _s.Report(b, "work", work);

        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.DismissReportAsync(_admin, r1)));

        Assert.Equal("dismissed", (await _s.LoadReport(r1)).Status);
        Assert.Equal("dismissed", (await _s.LoadReport(r2)).Status);
        Assert.False((await WorkTestKit.Load(_s.Host, work)).ReportHidden);
        Assert.Equal(ModerationOutcome.AlreadyResolved, await _s.Mod(m => m.DismissReportAsync(_admin, r1)));
        Assert.Equal("dismiss", (await _s.Audit()).Single().Action);
    }

    [Fact]
    public async Task Dismissing_an_auto_flag_while_the_content_is_still_blocked_is_refused()
    {
        var (_, work) = await _s.BlockedStory();
        var flag = await _s.AutoFlagId("work", work);
        Assert.Equal(ModerationOutcome.TargetStillBlocked, await _s.Mod(m => m.DismissReportAsync(_admin, flag)));
        Assert.Equal("pending", (await _s.LoadReport(flag)).Status);

        var (_, ok) = await _s.Story("Fine");
        var other = await _s.Report(await _s.NewUser(), "work", ok);
        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.DismissReportAsync(_admin, other)));
    }

    [Fact]
    public async Task Every_action_writes_one_audit_row_with_the_admin_target_reason_and_time()
    {
        var (owner, work) = await _s.Story();
        var fan = await _s.NewUser();
        var reply = await _s.ReplyOn(fan, work);
        var report = await _s.Report(fan, "work", work);
        var other = await _s.NewUser();
        var dismissable = await _s.Report(other, "user", fan.Id);

        await _s.Mod(m => m.DismissReportAsync(_admin, dismissable));
        _s.Host.Clock.Advance(TimeSpan.FromSeconds(1));
        await _s.Mod(m => m.HideReplyAsync(_admin, reply, "r-hide"));
        _s.Host.Clock.Advance(TimeSpan.FromSeconds(1));
        await _s.Mod(m => m.RemoveWorkAsync(_admin, work, "r-remove"));
        _s.Host.Clock.Advance(TimeSpan.FromSeconds(1));
        await _s.Mod(m => m.RestoreAsync(_admin, report, "r-restore"));
        _s.Host.Clock.Advance(TimeSpan.FromSeconds(1));
        await _s.Mod(m => m.SuspendUserAsync(_admin, owner.Id, "r-ban"));
        _s.Host.Clock.Advance(TimeSpan.FromSeconds(1));
        await _s.Mod(m => m.ReinstateUserAsync(_admin, owner.Id, "r-reinstate"));
        _s.Host.Clock.Advance(TimeSpan.FromSeconds(1));

        var rows = await _s.Audit();
        Assert.Equal(["dismiss", "hide_reply", "remove_work", "restore", "ban_user", "reinstate_user"], rows.Select(r => r.Action));
        Assert.All(rows, r => { Assert.Equal(_admin, r.AdminId); Assert.True(r.At >= ProfileTestHost.Now); });
        Assert.Equal([("report", dismissable), ("reply", reply), ("work", work), ("work", work), ("user", owner.Id), ("user", owner.Id)],
            rows.Select(r => (r.TargetType, r.TargetId)));
        Assert.Equal(["dismissed", "r-hide", "r-remove", "r-restore", "r-ban", "r-reinstate"], rows.Select(r => r.Reason));
    }

    [Fact]
    public async Task Audit_rows_survive_the_deletion_of_the_admin_account()
    {
        var (_, work) = await _s.Story();
        await _s.Mod(m => m.RemoveWorkAsync(_admin, work, "gone"));
        await using (var db = _s.Host.Db())
            await db.Users.Where(u => u.Id == _admin).ExecuteDeleteAsync();
        var rows = await _s.Audit();
        Assert.Single(rows);
        Assert.Equal(_admin, rows[0].AdminId);
    }
}
