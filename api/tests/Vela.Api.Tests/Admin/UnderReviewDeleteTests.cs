using System.Net;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data.Entities;
using Vela.Api.Images;
using Vela.Api.Tests.Support;
using Vela.Api.Tests.Works;
using static Vela.Api.Tests.Admin.ModerationKit;

namespace Vela.Api.Tests.Admin;

/// <summary>S1b-2 (a)(b): an owner cannot delete content that is waiting for moderation, nor a removed work with a legal record.</summary>
[Collection(PostgresCollection.Name)]
public class UnderReviewDeleteTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private Guid _admin;
    public async Task InitializeAsync() { _s = await AdminSite.Create(pg, isolated: true); _admin = await _s.NewAdminUser(); }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    private async Task AssertRefusedAndIntact(Profiles.TestUser owner, Guid work)
    {
        var before = _s.Host.Blobs.Names.Where(n => n.Contains(work.ToString())).ToList();
        var r = await owner.Delete($"/v1/works/{work}");
        Assert.Equal(HttpStatusCode.Conflict, r.Status);
        Assert.Equal("under_review", r.ErrorCode);
        await using var db = _s.Host.Db();
        Assert.True(await db.Works.AnyAsync(w => w.Id == work));
        Assert.True(await db.Chapters.AnyAsync(c => c.WorkId == work));
        Assert.Equal(before, _s.Host.Blobs.Names.Where(n => n.Contains(work.ToString())).ToList());
    }

    [Fact]
    public async Task A_pending_work_cannot_be_deleted()
    {
        var owner = await _s.NewUser();
        var work = await WorkTestKit.ReadyDraft(_s.Host, owner);
        await owner.Post($"/v1/works/{work}/publish", new { });

        await AssertRefusedAndIntact(owner, work);
    }

    [Fact]
    public async Task An_auto_blocked_work_cannot_be_deleted()
    {
        var (owner, work) = await _s.BlockedStory();
        Assert.Equal(WorkStatus.Blocked, (await WorkTestKit.Load(_s.Host, work)).Status);

        await AssertRefusedAndIntact(owner, work);
    }

    [Fact]
    public async Task A_work_hidden_by_a_report_cannot_be_deleted()
    {
        var (owner, work) = await _s.Story();
        await _s.Report(await _s.NewUser(), "work", work, "illegal_content");
        Assert.True((await WorkTestKit.Load(_s.Host, work)).ReportHidden);

        await AssertRefusedAndIntact(owner, work);
    }

    [Fact]
    public async Task A_removed_work_with_a_dmca_notice_cannot_be_deleted()
    {
        var (owner, work) = await _s.Story();
        await using (var db = _s.Host.Db())
        {
            db.DmcaNotices.Add(new DmcaNotice
            {
                Id = Guid.NewGuid(), WorkId = work, OwnerUserId = owner.Id, ClaimantName = "C", ClaimantEmail = "c@claim.test", ClaimantAddress = "A",
                WorkReference = work.ToString(), OriginalWorkDescription = "d", SignatureName = "C", GoodFaithStatement = true, PerjuryStatement = true,
                Status = DmcaStatus.Takedown, CreatedAt = ProfileTestHostNow,
            });
            await db.SaveChangesAsync();
        }
        await WorkTestKit.Modify(_s.Host, work, w => w.Status = WorkStatus.Removed);

        await AssertRefusedAndIntact(owner, work);
    }

    [Fact]
    public async Task A_work_under_an_evidence_hold_cannot_be_deleted()
    {
        // A hold normally comes with a ban (403 for everything); a hold on an owner who was later reinstated must still stand.
        var (owner, work) = await _s.Story();
        await using (var db = _s.Host.Db())
        {
            db.EvidenceHolds.Add(new EvidenceHold
            {
                Id = Guid.NewGuid(), UserId = owner.Id, WorkId = work, TargetType = "work", BlobPrefix = "evidence/x/", Reason = "r",
                HoldUntil = ProfileTestHostNow.AddDays(365), CreatedByAdminId = _admin, CreatedAt = ProfileTestHostNow,
            });
            await db.SaveChangesAsync();
        }

        await AssertRefusedAndIntact(owner, work);
    }

    [Fact]
    public async Task A_removed_work_without_a_legal_record_and_a_published_or_draft_work_can_still_be_deleted()
    {
        var (owner, removed) = await _s.Story();
        await WorkTestKit.Modify(_s.Host, removed, w => w.Status = WorkStatus.Removed);
        var (_, published) = await _s.Story(owner: owner);
        var draft = await WorkTestKit.Draft(_s.Host, owner);

        foreach (var id in new[] { removed, published, draft })
            Assert.Equal(HttpStatusCode.OK, (await owner.Delete($"/v1/works/{id}")).Status);
        Assert.DoesNotContain(_s.Host.Blobs.Names, n => n.Contains(published.ToString()) || n.Contains(removed.ToString()));
    }

    [Fact]
    public async Task An_auto_hidden_reply_cannot_be_deleted_by_its_author_or_the_work_owner()
    {
        var (artist, work) = await _s.Story();
        var author = await _s.NewUser();
        var reply = await _s.ReplyOn(author, work, "nasty " + Unsafe);
        Assert.Equal(HiddenBy.Auto, (await _s.LoadReply(reply)).HiddenBy);

        foreach (var who in new[] { author, artist })
        {
            var r = await who.Delete($"/v1/replies/{reply}");
            Assert.Equal(HttpStatusCode.Conflict, r.Status);
            Assert.Equal("under_review", r.ErrorCode);
        }
        Assert.NotNull(await _s.LoadReply(reply));
    }

    [Fact]
    public async Task A_report_hidden_reply_cannot_be_deleted_but_an_ordinary_one_can()
    {
        var (_, work) = await _s.Story();
        var author = await _s.NewUser();
        var flagged = await _s.ReplyOn(author, work, "rude words");
        await _s.Report(await _s.NewUser(), "reply", flagged, "harassment");
        var fine = await _s.ReplyOn(author, work, "lovely");

        Assert.Equal("under_review", (await author.Delete($"/v1/replies/{flagged}")).ErrorCode);
        Assert.Equal(HttpStatusCode.OK, (await author.Delete($"/v1/replies/{fine}")).Status);
    }

    private static readonly DateTimeOffset ProfileTestHostNow = Profiles.ProfileTestHost.Now;
}
