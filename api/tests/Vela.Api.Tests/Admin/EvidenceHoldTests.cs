using Microsoft.EntityFrameworkCore;
using Vela.Api.Admin;
using Vela.Api.Data.Entities;
using Vela.Api.Images;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using Vela.Api.Tests.Works;
using static Vela.Api.Tests.Admin.ModerationKit;

namespace Vela.Api.Tests.Admin;

[Collection(PostgresCollection.Name)]
public class EvidenceHoldTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private Guid _admin;
    public async Task InitializeAsync() { _s = await AdminSite.Create(pg, isolated: true); _admin = await _s.NewAdminUser(); }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    private static readonly DateTimeOffset Now = ProfileTestHost.Now;

    [Fact]
    public async Task Marking_a_work_snapshots_it_copies_the_blobs_takes_it_down_bans_the_owner_and_resolves_the_reports()
    {
        var owner = await _s.OwnerWithEmail();
        var (_, work) = await _s.Story("Hold this title", owner);
        var original = _s.Host.Blobs.Bytes(await WorkTestKit.OriginalOf(_s.Host, work));
        var fan = await _s.NewUser();
        var report = await _s.Report(fan, "work", work, "illegal_content", "private reporter note");
        var second = await _s.Report(await _s.NewUser(), "work", work, "spam");

        var result = await _s.MarkIllegal(_admin, report, "  clear violation  ");

        Assert.Equal(ModerationOutcome.Ok, result.Outcome);
        Assert.False(result.BanRefused);
        var hold = await _s.Hold(result.HoldId!.Value);
        Assert.Equal("work", hold.TargetType);
        Assert.Equal(work, hold.WorkId);
        Assert.Null(hold.ReplyId);
        Assert.Equal(owner.Id, hold.UserId);
        Assert.Equal(_admin, hold.CreatedByAdminId);
        Assert.Equal("clear violation", hold.Reason);
        Assert.Equal(Now.AddDays(365), hold.HoldUntil);
        Assert.Equal(Now, hold.CreatedAt);
        Assert.Equal($"evidence/{hold.Id}/", hold.BlobPrefix);
        Assert.Null(hold.NcmecReportId);

        var snap = hold.Snapshot();
        Assert.Equal("work", snap.GetProperty("targetType").GetString());
        Assert.Equal(owner.Id, snap.GetProperty("owner").GetProperty("userId").GetGuid());
        Assert.False(string.IsNullOrEmpty(snap.GetProperty("owner").GetProperty("handle").GetString()));
        Assert.Equal("Test Artist", snap.GetProperty("owner").GetProperty("displayName").GetString());
        var identity = Assert.Single(snap.GetProperty("owner").GetProperty("identities").EnumerateArray(), i => i.GetProperty("provider").GetString() == "email");
        Assert.Equal(EvidenceKit.OwnerEmail, identity.GetProperty("email").GetString());
        var w = snap.GetProperty("work");
        Assert.Equal("Hold this title", w.GetProperty("title").GetString());
        var chapter = w.GetProperty("chapters")[0];
        Assert.Equal(0.5, chapter.GetProperty("x").GetDouble());
        Assert.Equal(0.5, chapter.GetProperty("y").GetDouble());
        Assert.Equal("Body", chapter.GetProperty("body").GetString());
        var reports = snap.GetProperty("reports").EnumerateArray().ToList();
        Assert.Equal(2, reports.Count);
        Assert.Contains(reports, r => r.GetProperty("id").GetGuid() == report && r.GetProperty("reason").GetString() == "illegal_content");
        Assert.DoesNotContain("private reporter note", hold.Snapshot().ToString());

        Assert.Equal(original, _s.Host.Blobs.Bytes($"evidence/{hold.Id}/original"));
        foreach (var v in new[] { "thumb", "feed", "full" })
            Assert.True(_s.Host.Blobs.Exists($"evidence/{hold.Id}/variants/{v}.webp"), v);

        Assert.Equal(WorkStatus.Removed, (await WorkTestKit.Load(_s.Host, work)).Status);
        foreach (var v in Variants(work)) Assert.False(_s.Host.Blobs.Exists(v));
        Assert.True(_s.Host.Blobs.Exists(await WorkTestKit.OriginalOf(_s.Host, work)));
        Assert.NotNull((await _s.LoadUser(owner.Id)).SuspendedAt);
        Assert.Equal("actioned", (await _s.LoadReport(report)).Status);
        Assert.Equal("actioned", (await _s.LoadReport(second)).Status);
        var audit = (await _s.Audit()).Select(a => (a.Action, a.TargetType, a.TargetId)).ToList();
        Assert.Contains(("mark_illegal", "work", work), audit);
        Assert.Contains(("remove_work", "work", work), audit);
        Assert.Contains(("ban_user", "user", owner.Id), audit);
    }

    [Fact]
    public async Task Marking_a_reply_snapshots_the_text_hides_it_and_bans_the_author_but_leaves_the_story()
    {
        var (_, work) = await _s.Story();
        var author = await _s.OwnerWithEmail();
        var reply = await _s.ReplyOn(author, work, "the offending reply text");
        var report = await _s.Report(await _s.NewUser(), "reply", reply, "illegal_content");

        var result = await _s.MarkIllegal(_admin, report);

        Assert.Equal(ModerationOutcome.Ok, result.Outcome);
        var hold = await _s.Hold(result.HoldId!.Value);
        Assert.Equal(("reply", reply, author.Id), (hold.TargetType, hold.ReplyId, hold.UserId));
        Assert.Null(hold.WorkId);
        var r = hold.Snapshot().GetProperty("reply");
        Assert.Equal("the offending reply text", r.GetProperty("text").GetString());
        Assert.Equal(work, r.GetProperty("workId").GetGuid());
        Assert.Equal(0, r.GetProperty("chapterIndex").GetInt32());
        Assert.Equal(author.Id, r.GetProperty("authorId").GetGuid());
        Assert.False(string.IsNullOrEmpty(r.GetProperty("authorHandle").GetString()));
        Assert.Equal("", _s.EvidenceBlobs(hold.Id));

        var saved = await _s.LoadReply(reply);
        Assert.True(saved.Hidden);
        Assert.Equal(HiddenBy.Admin, saved.HiddenBy);
        Assert.NotNull((await _s.LoadUser(author.Id)).SuspendedAt);
        Assert.Equal(WorkStatus.Published, (await WorkTestKit.Load(_s.Host, work)).Status);
        Assert.Equal("actioned", (await _s.LoadReport(report)).Status);
        Assert.Contains(await _s.Audit(), a => a.Action == "mark_illegal" && a.TargetType == "reply" && a.TargetId == reply);
    }

    [Fact]
    public async Task Marking_a_user_snapshots_the_profile_and_identities_and_bans_the_account()
    {
        var target = await _s.OwnerWithEmail();
        await using (var db = _s.Host.Db())
            await db.Profiles.Where(p => p.UserId == target.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Bio, "a bio worth keeping").SetProperty(p => p.City, "Springfield"));
        var report = await _s.Report(await _s.NewUser(), "user", target.Id, "illegal_content");

        var result = await _s.MarkIllegal(_admin, report);

        Assert.Equal(ModerationOutcome.Ok, result.Outcome);
        var hold = await _s.Hold(result.HoldId!.Value);
        Assert.Equal(("user", target.Id), (hold.TargetType, hold.UserId));
        Assert.Null(hold.WorkId);
        Assert.Null(hold.ReplyId);
        var p = hold.Snapshot().GetProperty("owner").GetProperty("profile");
        Assert.Equal("a bio worth keeping", p.GetProperty("bio").GetString());
        Assert.Equal("Springfield", p.GetProperty("city").GetString());
        Assert.Contains(hold.Snapshot().GetProperty("owner").GetProperty("identities").EnumerateArray(), i => i.GetProperty("email").GetString() == EvidenceKit.OwnerEmail);
        Assert.NotNull((await _s.LoadUser(target.Id)).SuspendedAt);
        Assert.Equal("actioned", (await _s.LoadReport(report)).Status);
    }

    [Fact]
    public async Task The_snapshot_never_holds_the_raw_sign_in_subject()
    {
        var (owner, work) = await _s.Story();
        string subject;
        await using (var db = _s.Host.Db())
            subject = await db.Identities.Where(i => i.UserId == owner.Id).Select(i => i.Subject).FirstAsync();
        var report = await _s.Report(await _s.NewUser(), "work", work, "illegal_content");

        var hold = await _s.Hold((await _s.MarkIllegal(_admin, report)).HoldId!.Value);

        Assert.DoesNotContain(subject, hold.Snapshot().ToString());
    }

    [Fact]
    public async Task Marking_the_same_target_again_returns_the_existing_hold_and_changes_nothing_more()
    {
        var (_, work) = await _s.Story();
        var report = await _s.Report(await _s.NewUser(), "work", work, "illegal_content");
        var otherReport = await _s.Report(await _s.NewUser(), "work", work, "spam");
        var first = await _s.MarkIllegal(_admin, report);
        int Actions() => _s.Audit().Result.Count(a => a.Action is "mark_illegal" or "remove_work" or "ban_user");
        var auditBefore = Actions();

        var again = await _s.MarkIllegal(_admin, report, "second try");
        var viaNewReport = await _s.MarkIllegal(_admin, otherReport, "third");

        Assert.Equal(ModerationOutcome.Ok, again.Outcome);
        Assert.Equal(first.HoldId, again.HoldId);
        Assert.Equal(first.HoldId, viaNewReport.HoldId);
        Assert.Single(await _s.Holds());
        Assert.Single(await _s.Audit(), a => a.Action == "mark_illegal");
        Assert.Equal("clear violation", (await _s.Hold(first.HoldId!.Value)).Reason);
        Assert.Equal(auditBefore, Actions());
    }

    [Fact]
    public async Task An_admin_owner_gets_a_hold_and_the_takedown_but_is_never_banned()
    {
        var boss = await _s.AdminOwner();
        var (_, work) = await _s.Story("Admin story", boss);
        var report = await _s.Report(await _s.NewUser(), "work", work, "illegal_content");

        var result = await _s.MarkIllegal(_admin, report);

        Assert.Equal(ModerationOutcome.Ok, result.Outcome);
        Assert.True(result.BanRefused);
        Assert.NotNull(result.HoldId);
        Assert.Null((await _s.LoadUser(boss.Id)).SuspendedAt);
        Assert.Equal(WorkStatus.Removed, (await WorkTestKit.Load(_s.Host, work)).Status);
        Assert.DoesNotContain(await _s.Audit(), a => a.Action == "ban_user");
        Assert.Equal("actioned", (await _s.LoadReport(report)).Status);
    }

    [Fact]
    public async Task A_failed_copy_commits_no_hold_and_takes_nothing_down()
    {
        var owner = await _s.OwnerWithEmail();
        var (_, work) = await _s.Story("Keep me up", owner);
        var report = await _s.Report(await _s.NewUser(), "work", work, "illegal_content");
        _s.Host.Blobs.FailWrites = n => n.StartsWith("evidence/", StringComparison.Ordinal);

        var result = await _s.MarkIllegal(_admin, report);

        Assert.Equal(ModerationOutcome.EvidenceCopyFailed, result.Outcome);
        Assert.Null(result.HoldId);
        Assert.Empty(await _s.Holds());
        Assert.Equal(WorkStatus.Published, (await WorkTestKit.Load(_s.Host, work)).Status);
        Assert.Null((await _s.LoadUser(owner.Id)).SuspendedAt);
        Assert.Equal("pending", (await _s.LoadReport(report)).Status);
        Assert.Empty(await _s.Audit());
        Assert.DoesNotContain(_s.Host.Blobs.Names, n => n.StartsWith("evidence/", StringComparison.Ordinal));
        foreach (var v in Variants(work)) Assert.True(_s.Host.Blobs.Exists(v));

        _s.Host.Blobs.FailWrites = null;
        Assert.Equal(ModerationOutcome.Ok, (await _s.MarkIllegal(_admin, report)).Outcome);
    }

    [Fact]
    public async Task A_copy_that_fails_halfway_leaves_no_partial_evidence_blobs_behind()
    {
        var (_, work) = await _s.Story();
        var report = await _s.Report(await _s.NewUser(), "work", work, "illegal_content");
        _s.Host.Blobs.FailWrites = n => n.EndsWith("/variants/full.webp", StringComparison.Ordinal);

        var result = await _s.MarkIllegal(_admin, report);

        Assert.Equal(ModerationOutcome.EvidenceCopyFailed, result.Outcome);
        Assert.DoesNotContain(_s.Host.Blobs.Names, n => n.StartsWith("evidence/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Public_variants_that_are_already_gone_are_skipped_but_the_original_is_still_held()
    {
        var (_, work) = await _s.Story();
        foreach (var v in Variants(work)) await _s.Host.Blobs.DeletePrefixAsync(v);
        var report = await _s.Report(await _s.NewUser(), "work", work, "illegal_content");

        var result = await _s.MarkIllegal(_admin, report);

        Assert.Equal(ModerationOutcome.Ok, result.Outcome);
        Assert.Equal($"evidence/{result.HoldId}/original", _s.EvidenceBlobs(result.HoldId!.Value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_reason_is_required_and_a_refused_mark_changes_nothing(string reason)
    {
        var (_, work) = await _s.Story();
        var report = await _s.Report(await _s.NewUser(), "work", work, "illegal_content");

        Assert.Equal(ModerationOutcome.ReasonRequired, (await _s.MarkIllegal(_admin, report, reason)).Outcome);
        Assert.Equal(ModerationOutcome.ReasonTooLong, (await _s.MarkIllegal(_admin, report, new string('x', 501))).Outcome);

        Assert.Empty(await _s.Holds());
        Assert.Equal(WorkStatus.Published, (await WorkTestKit.Load(_s.Host, work)).Status);
        Assert.Empty(await _s.Audit());
    }

    [Fact]
    public async Task Only_a_live_admin_can_mark_and_an_unknown_report_is_not_found()
    {
        var (_, work) = await _s.Story();
        var report = await _s.Report(await _s.NewUser(), "work", work, "illegal_content");
        var plain = await _s.NewUser();

        Assert.Equal(ModerationOutcome.Forbidden, (await _s.MarkIllegal(plain.Id, report)).Outcome);
        Assert.Equal(ModerationOutcome.NotFound, (await _s.MarkIllegal(_admin, Guid.NewGuid())).Outcome);
        Assert.Empty(await _s.Holds());
    }

    [Fact]
    public async Task A_report_whose_target_is_gone_is_not_found_and_holds_nothing()
    {
        var (owner, work) = await _s.Story();
        var report = await _s.Report(await _s.NewUser(), "work", work, "illegal_content");
        await using (var db = _s.Host.Db()) await db.Works.Where(w => w.Id == work).ExecuteDeleteAsync();

        Assert.Equal(ModerationOutcome.NotFound, (await _s.MarkIllegal(_admin, report)).Outcome);
        Assert.Empty(await _s.Holds());
        Assert.Null((await _s.LoadUser(owner.Id)).SuspendedAt);
    }
}
