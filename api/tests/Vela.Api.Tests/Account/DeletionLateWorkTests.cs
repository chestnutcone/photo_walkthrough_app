using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Account;
using Vela.Api.Data.Entities;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Account.AccountKit;

namespace Vela.Api.Tests.Account;

/// <summary>Works and uploads that appear while the blob/CDN phase is running must still be cleaned, and never be orphaned.</summary>
[Collection(PostgresCollection.Name)]
public class DeletionLateWorkTests(PostgresFixture pg) : IDisposable
{
    private readonly List<ProfileTestHost> _hosts = [];
    private readonly RecordingCdnPurger _cdn = new();
    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private async Task<(ProfileTestHost Host, string Cs, World World)> Setup()
    {
        var cs = await pg.CreateIsolatedDatabaseAsync();
        var h = NewHost(pg, cs, _cdn);
        _hosts.Add(h);
        var w = await Seed(h);
        await RequestDelete(w.U);
        return (h, cs, w);
    }

    /// <summary>A published work for the user, with blobs in the works and originals containers.</summary>
    private static async Task<Guid> AddLateWork(ProfileTestHost h, Guid artist, bool fullBlobs = true)
    {
        var id = Guid.NewGuid();
        await using (var db = h.Db())
        {
            db.Works.Add(new Work { Id = id, ArtistId = artist, Title = "late", Status = WorkStatus.Published, ImageBlob = "b", Width = 3, Height = 4, CreatedAt = h.Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }
        h.Blobs.Put($"works/{id}/thumb.webp", [1]);
        if (fullBlobs) { h.Blobs.Put($"works/{id}/full.webp", [2]); h.Blobs.Put($"originals/works/{id}", [3]); }
        return id;
    }

    private static List<string> Orphans(ProfileTestHost h, Guid user, params Guid[] works) =>
        h.Blobs.Names.Where(n => n.Contains(user.ToString()) || works.Any(w => n.Contains(w.ToString()))).ToList();

    private void OnFirstPurge(Func<Task> action)
    {
        var fired = false;
        _cdn.OnPurge = async () => { if (fired) return; fired = true; await action(); };
    }

    [Fact]
    public async Task A_work_created_during_the_blob_phase_is_cleaned_and_no_blob_remains()
    {
        var (h, cs, w) = await Setup();
        var late = Guid.Empty;
        OnFirstPurge(async () => late = await AddLateWork(h, w.U.Id));

        await RunJob(h, w.U.Id);

        Assert.Equal(0, await Count(cs, "users", "id", w.U.Id));
        Assert.Equal(0, await Count(cs, "works", "id", late));
        Assert.Empty(Orphans(h, w.U.Id, w.UWork, late));
        Assert.Contains(_cdn.Purged, p => p.Contains($"works/{late}/"));
        Assert.Contains($"works/{w.OWork}/thumb.webp", h.Blobs.Names); // others untouched
    }

    [Fact]
    public async Task A_work_and_an_avatar_and_an_export_written_during_the_blob_phase_are_all_cleaned()
    {
        var (h, cs, w) = await Setup();
        var late = Guid.Empty;
        OnFirstPurge(async () =>
        {
            late = await AddLateWork(h, w.U.Id);
            h.Blobs.Put($"avatars/{w.U.Id}/avatar-256.webp", [7]);
            h.Blobs.Put($"originals/avatars/{w.U.Id}", [7]);
            h.Blobs.Put($"exports/{w.U.Id}/{Guid.NewGuid()}.zip", [7]);
        });

        await RunJob(h, w.U.Id);

        Assert.Equal(0, await Count(cs, "users", "id", w.U.Id));
        Assert.Empty(Orphans(h, w.U.Id, w.UWork, late));
    }

    [Fact]
    public async Task A_failure_cleaning_a_late_work_keeps_the_rows_and_the_next_sweep_finishes_without_orphans()
    {
        var (h, cs, w) = await Setup();
        var late = Guid.Empty;
        OnFirstPurge(async () => { late = await AddLateWork(h, w.U.Id); h.Blobs.FailDeletes = p => p.Contains(late.ToString()); });
        var worker = h.Services.GetRequiredService<DeletionWorker>();

        Assert.Equal(0, await worker.RunOnceAsync(h.Clock.GetUtcNow(), CancellationToken.None));

        Assert.Equal(1, await Count(cs, "users", "id", w.U.Id)); // nothing was deleted, so the retry can find the late work
        Assert.Equal(1, await Count(cs, "works", "id", late));
        Assert.Equal("1", (await Query(cs, $"SELECT deletion_failures FROM users WHERE id = '{w.U.Id}'")).Single()[0]);
        Assert.Contains($"works/{late}/full.webp", h.Blobs.Names);

        h.Blobs.FailDeletes = null;
        h.Clock.Advance(DeletionWorker.Backoff + TimeSpan.FromSeconds(1));
        Assert.Equal(1, await worker.RunOnceAsync(h.Clock.GetUtcNow(), CancellationToken.None));
        Assert.Equal(0, await Count(cs, "users", "id", w.U.Id));
        Assert.Empty(Orphans(h, w.U.Id, w.UWork, late));
    }

    [Fact]
    public async Task A_late_work_whose_blobs_are_already_half_deleted_is_handled_idempotently()
    {
        var (h, cs, w) = await Setup();
        var late = Guid.Empty;
        OnFirstPurge(async () => late = await AddLateWork(h, w.U.Id, fullBlobs: false)); // only the thumbnail is left

        await RunJob(h, w.U.Id);
        await RunJob(h, w.U.Id); // and a repeat is a no-op

        Assert.Equal(0, await Count(cs, "users", "id", w.U.Id));
        Assert.Empty(Orphans(h, w.U.Id, w.UWork, late));
    }

    [Fact]
    public async Task Works_that_keep_appearing_end_in_a_recorded_failure_with_the_user_row_intact()
    {
        var (h, cs, w) = await Setup();
        _cdn.OnPurge = async () => await AddLateWork(h, w.U.Id);
        var worker = h.Services.GetRequiredService<DeletionWorker>();

        Assert.Equal(0, await worker.RunOnceAsync(h.Clock.GetUtcNow(), CancellationToken.None));

        Assert.Equal(1, await Count(cs, "users", "id", w.U.Id));
        Assert.Equal("1", (await Query(cs, $"SELECT deletion_failures FROM users WHERE id = '{w.U.Id}'")).Single()[0]);
        _cdn.OnPurge = null;
        h.Clock.Advance(DeletionWorker.Backoff + TimeSpan.FromSeconds(1));
        Assert.Equal(1, await worker.RunOnceAsync(h.Clock.GetUtcNow(), CancellationToken.None));
        Assert.Empty(Orphans(h, w.U.Id, w.UWork));
        Assert.Equal(0, await Count(cs, "works", "artist_id", w.U.Id));
    }
}
