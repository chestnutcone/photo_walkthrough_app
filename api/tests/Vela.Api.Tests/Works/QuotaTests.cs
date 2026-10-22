using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vela.Api.Data.Entities;
using Vela.Api.Images;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using Vela.Api.Works;

namespace Vela.Api.Tests.Works;

[Collection(PostgresCollection.Name)]
public class QuotaTests(PostgresFixture pg) : IDisposable
{
    private readonly ProfileTestHost _host = new(pg);
    public void Dispose() => _host.Dispose();

    private async Task<TestUser> Artist() => await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());

    private async Task Seed(Guid userId, WorkStatus status, int count)
    {
        await using var db = _host.Db();
        for (var i = 0; i < count; i++)
            db.Works.Add(new Work { Id = Guid.NewGuid(), ArtistId = userId, Status = status, ImageBlob = "x", CreatedAt = ProfileTestHost.Now });
        await db.SaveChangesAsync();
    }

    private static object Body => new { contentType = "image/jpeg" };

    [Fact]
    public async Task Twenty_first_draft_is_refused_with_quota_exceeded()
    {
        var u = await Artist();
        await Seed(u.Id, WorkStatus.Published, 20);

        var r = await u.Post("/v1/works", Body);

        Assert.Equal(HttpStatusCode.Conflict, r.Status);
        Assert.Equal("quota_exceeded", r.ErrorCode);
        Assert.Contains("20", r.Body.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Draft_creation_returns_upload_ticket_and_a_draft_expiring_in_24_hours()
    {
        var u = await Artist();

        var r = await u.Post("/v1/works", Body);

        Assert.Equal(HttpStatusCode.OK, r.Status);
        var id = r.Data.GetProperty("workId").GetGuid();
        Assert.Contains($"originals/works/{id}", r.Data.GetProperty("uploadUrl").GetString());
        Assert.Equal(ProfileTestHost.Now.AddMinutes(15), r.Data.GetProperty("expiresAt").GetDateTimeOffset());
        await using var db = _host.Db();
        var w = await db.Works.SingleAsync(x => x.Id == id);
        Assert.Equal(WorkStatus.Draft, w.Status);
        Assert.Equal(ProfileTestHost.Now.AddHours(24), w.ExpiresAt);
        Assert.Equal($"originals/works/{id}", w.ImageBlob);
        Assert.Equal((0, 0), (w.Width, w.Height));
        Assert.Equal(u.Id, w.ArtistId);
    }

    [Fact]
    public async Task Draft_creation_requires_onboarding_auth_and_a_valid_content_type()
    {
        var fresh = await _host.SignIn();
        Assert.Equal("onboarding_required", (await fresh.Post("/v1/works", Body)).ErrorCode);

        var u = await Artist();
        var bad = await u.Post("/v1/works", new { contentType = "image/gif" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
        Assert.Equal("validation_failed", bad.ErrorCode);

        var anon = await _host.Anonymous().PostAsync("/v1/works", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
    }

    [Fact]
    public async Task Two_concurrent_drafts_at_19_yield_exactly_one_success()
    {
        var u = await Artist();
        await Seed(u.Id, WorkStatus.Published, 19);

        var replies = await Task.WhenAll(u.Post("/v1/works", Body), u.Post("/v1/works", Body));

        Assert.Single(replies, r => r.Status == HttpStatusCode.OK);
        Assert.Single(replies, r => r.ErrorCode == "quota_exceeded");
        await using var db = _host.Db();
        Assert.Equal(20, await new WorkCounts(db).CountActiveAsync(u.Id));
    }

    [Fact]
    public async Task Deleting_a_work_frees_a_slot()
    {
        var u = await Artist();
        await Seed(u.Id, WorkStatus.Published, 20);
        Assert.Equal("quota_exceeded", (await u.Post("/v1/works", Body)).ErrorCode);

        await using (var db = _host.Db())
            await db.Works.Where(w => w.ArtistId == u.Id).Take(1).ExecuteDeleteAsync();

        Assert.Equal(HttpStatusCode.OK, (await u.Post("/v1/works", Body)).Status);
    }

    [Fact]
    public async Task Blocked_and_removed_works_do_not_count()
    {
        var u = await Artist();
        await Seed(u.Id, WorkStatus.Blocked, 10);
        await Seed(u.Id, WorkStatus.Removed, 10);
        await Seed(u.Id, WorkStatus.Pending, 19);

        Assert.Equal(HttpStatusCode.OK, (await u.Post("/v1/works", Body)).Status);
        Assert.Equal("quota_exceeded", (await u.Post("/v1/works", Body)).ErrorCode);
    }

    [Fact]
    public async Task Expired_drafts_are_deleted_with_their_blobs_by_the_cleanup_job()
    {
        var u = await Artist();
        await Seed(u.Id, WorkStatus.Published, 1);
        await Seed(u.Id, WorkStatus.Pending, 1);
        var draft = (await u.Post("/v1/works", Body)).Data.GetProperty("workId").GetGuid();
        var fresh = (await u.Post("/v1/works", Body)).Data.GetProperty("workId").GetGuid();
        _host.Blobs.Put($"originals/works/{draft}", [1]);
        _host.Blobs.Put($"works/{draft}/thumb.webp", [1]);
        _host.Blobs.Put($"works/{draft}/feed.webp", [1]);
        _host.Blobs.Put($"originals/works/{fresh}", [1]);
        await using (var db = _host.Db())
            await db.Works.Where(w => w.Id == draft).ExecuteUpdateAsync(s => s.SetProperty(w => w.ExpiresAt, ProfileTestHost.Now.AddHours(1)));
        var job = _host.Services.GetServices<IHostedService>().OfType<DraftCleanupJob>().Single();

        await job.RunOnceAsync(ProfileTestHost.Now.AddHours(2), CancellationToken.None);

        await using var check = _host.Db();
        Assert.False(await check.Works.AnyAsync(w => w.Id == draft));
        Assert.True(await check.Works.AnyAsync(w => w.Id == fresh));
        Assert.Equal(3, await check.Works.CountAsync(w => w.ArtistId == u.Id));
        Assert.False(_host.Blobs.Exists($"originals/works/{draft}"));
        Assert.False(_host.Blobs.Exists($"works/{draft}/thumb.webp"));
        Assert.False(_host.Blobs.Exists($"works/{draft}/feed.webp"));
        Assert.True(_host.Blobs.Exists($"originals/works/{fresh}"));
    }

    [Fact]
    public async Task Cleanup_never_touches_published_or_pending_works_even_with_a_past_expiry()
    {
        var u = await Artist();
        await Seed(u.Id, WorkStatus.Published, 1);
        await Seed(u.Id, WorkStatus.Pending, 1);
        await using (var db = _host.Db())
            await db.Works.Where(w => w.ArtistId == u.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.ExpiresAt, ProfileTestHost.Now));
        var job = _host.Services.GetServices<IHostedService>().OfType<DraftCleanupJob>().Single();

        await job.RunOnceAsync(ProfileTestHost.Now.AddDays(5), CancellationToken.None);

        await using var check = _host.Db();
        Assert.Equal(2, await check.Works.CountAsync(w => w.ArtistId == u.Id));
    }

    [Fact]
    public async Task Cleanup_processes_at_most_100_drafts_per_run()
    {
        var u = await Artist();
        await Seed(u.Id, WorkStatus.Draft, 120);
        await using (var db = _host.Db())
            await db.Works.Where(w => w.ArtistId == u.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.ExpiresAt, ProfileTestHost.Now));
        var job = _host.Services.GetServices<IHostedService>().OfType<DraftCleanupJob>().Single();

        await job.RunOnceAsync(ProfileTestHost.Now.AddHours(1), CancellationToken.None);

        await using var check = _host.Db();
        Assert.Equal(20, await check.Works.CountAsync(w => w.ArtistId == u.Id));
    }
}
