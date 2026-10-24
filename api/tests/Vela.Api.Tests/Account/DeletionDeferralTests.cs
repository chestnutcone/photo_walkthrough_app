using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Account;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Account.AccountKit;

namespace Vela.Api.Tests.Account;

/// <summary>
/// S1b-2 (c): an account with content under review is hidden and signed out at once, but its hard delete waits until the reviews
/// are resolved or 30 days after the request, whichever comes first.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DeletionDeferralTests(PostgresFixture pg) : IDisposable
{
    private readonly List<ProfileTestHost> _hosts = [];
    private static readonly DateTimeOffset Now = ProfileTestHost.Now;

    private async Task<ProfileTestHost> Host()
    {
        var h = NewHost(pg, await pg.CreateIsolatedDatabaseAsync());
        _hosts.Add(h);
        return h;
    }

    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private static Task<int> Sweep(ProfileTestHost h) =>
        h.Services.GetRequiredService<DeletionWorker>().RunOnceAsync(h.Clock.GetUtcNow(), CancellationToken.None);

    private static async Task<User?> LoadUser(ProfileTestHost h, Guid id)
    {
        await using var db = h.Db();
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);
    }

    private static async Task<Guid> AddWork(ProfileTestHost h, Guid owner, WorkStatus status)
    {
        var id = Guid.NewGuid();
        await using var db = h.Db();
        db.Works.Add(new Work { Id = id, ArtistId = owner, Title = "t", Status = status, ImageBlob = $"originals/works/{id}", CreatedAt = Now });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Without_open_reviews_deletion_is_scheduled_and_runs_on_the_next_sweep()
    {
        var h = await Host();
        var u = await Onboarded(h, null, "Plain");

        var r = await u.Delete("/v1/me");

        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal("scheduled", r.Data.GetProperty("state").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, r.Data.GetProperty("completesBy").ValueKind);
        Assert.Equal(1, await Sweep(h));
        Assert.Null(await LoadUser(h, u.Id));
    }

    [Fact]
    public async Task A_pending_work_defers_the_hard_delete_until_it_is_resolved()
    {
        var h = await Host();
        var u = await Onboarded(h, null, "Pending");
        var work = await AddWork(h, u.Id, WorkStatus.Pending);

        var r = await u.Delete("/v1/me");

        Assert.Equal("deferred_review", r.Data.GetProperty("state").GetString());
        Assert.Equal(Now.AddDays(30), r.Data.GetProperty("completesBy").GetDateTimeOffset());
        Assert.Equal(HttpStatusCode.Forbidden, (await u.Get("/v1/me")).Status); // signed out and hidden at once, as before

        Assert.Equal(0, await Sweep(h));
        var user = await LoadUser(h, u.Id);
        Assert.NotNull(user);
        Assert.Equal(Now.AddDays(30), user!.DeletionDeferredUntil);
        await using (var db = h.Db())
        {
            Assert.True(await db.Works.AnyAsync(w => w.Id == work));
            await db.Works.Where(w => w.Id == work).ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkStatus.Published));
        }

        Assert.Equal(0, await Sweep(h)); // not re-checked before the recheck interval
        h.Clock.Advance(DeletionWorker.DeferredRecheck + TimeSpan.FromSeconds(1));
        Assert.Equal(1, await Sweep(h));
        Assert.Null(await LoadUser(h, u.Id));
    }

    [Fact]
    public async Task An_open_report_against_a_reply_defers_the_hard_delete()
    {
        var h = await Host();
        var u = await Onboarded(h, null, "Replier");
        var other = await Onboarded(h, null, "Other");
        var work = await AddWork(h, other.Id, WorkStatus.Published);
        await using (var db = h.Db())
        {
            var reply = new Vela.Api.Data.Entities.Reply { Id = Guid.NewGuid(), WorkId = work, ChapterIndex = 0, AuthorId = u.Id, Text = "x", CreatedAt = Now };
            db.Replies.Add(reply);
            db.Reports.Add(new Report { Id = Guid.NewGuid(), Source = "user_report", Reason = "harassment", TargetType = "reply", TargetId = reply.Id, ReporterId = other.Id, CreatedAt = Now });
            await db.SaveChangesAsync();
        }

        Assert.Equal("deferred_review", (await u.Delete("/v1/me")).Data.GetProperty("state").GetString());
        Assert.Equal(0, await Sweep(h));
        Assert.NotNull(await LoadUser(h, u.Id));
    }

    [Fact]
    public async Task A_dmca_notice_or_an_evidence_hold_defers_for_at_most_30_days_and_both_records_survive_the_delete()
    {
        var h = await Host();
        var u = await Onboarded(h, null, "Held");
        var work = await AddWork(h, u.Id, WorkStatus.Removed);
        Guid notice = Guid.NewGuid(), hold = Guid.NewGuid();
        await using (var db = h.Db())
        {
            db.DmcaNotices.Add(new DmcaNotice
            {
                Id = notice, WorkId = work, OwnerUserId = u.Id, ClaimantName = "C", ClaimantEmail = "c@claim.test", ClaimantAddress = "A", WorkReference = "r",
                OriginalWorkDescription = "d", SignatureName = "C", GoodFaithStatement = true, PerjuryStatement = true, Status = DmcaStatus.Takedown, CreatedAt = Now,
            });
            db.EvidenceHolds.Add(new EvidenceHold
            {
                Id = hold, UserId = u.Id, WorkId = work, TargetType = "work", BlobPrefix = "evidence/x/", Reason = "r", HoldUntil = Now.AddDays(365),
                CreatedByAdminId = Guid.NewGuid(), CreatedAt = Now,
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal("deferred_review", (await u.Delete("/v1/me")).Data.GetProperty("state").GetString());
        h.Clock.Advance(TimeSpan.FromDays(30) - TimeSpan.FromMinutes(1));
        Assert.Equal(0, await Sweep(h));
        Assert.NotNull(await LoadUser(h, u.Id));

        h.Clock.Advance(TimeSpan.FromMinutes(2)); // the 30-day cap has passed: the delete proceeds although nothing was resolved
        Assert.Equal(1, await Sweep(h));
        Assert.Null(await LoadUser(h, u.Id));
        await using var after = h.Db();
        Assert.True(await after.DmcaNotices.AnyAsync(n => n.Id == notice));
        Assert.True(await after.EvidenceHolds.AnyAsync(x => x.Id == hold));
    }
}
