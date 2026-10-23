using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Admin;
using Vela.Api.Common;
using Vela.Api.Data.Entities;
using Vela.Api.Moderation;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;
using Vela.Api.Tests.Works;
using static Vela.Api.Tests.Admin.ModerationKit;

namespace Vela.Api.Tests.Admin;

public sealed class ToggleFailingHandler : IDomainEventHandler<WorkPublished>
{
    public static volatile bool Fail;
    public Task HandleAsync(WorkPublished evt, CancellationToken ct) => Fail ? throw new InvalidOperationException("handler failed") : Task.CompletedTask;
}

[Collection(PostgresCollection.Name)]
public class TakedownReconcilerTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private AdminSite _s = null!;
    private Guid _admin;
    public TakedownReconcilerTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        ToggleFailingHandler.Fail = false;
        _s = await AdminSite.Create(_pg, isolated: true, extra: s => s.AddScoped<IDomainEventHandler<WorkPublished>, ToggleFailingHandler>());
        _admin = await _s.NewAdminUser();
    }
    public Task DisposeAsync() { ToggleFailingHandler.Fail = false; _s.Dispose(); return Task.CompletedTask; }

    private Task<int> Reconcile() => _s.Host.Services.GetRequiredService<TakedownReconciler>().RunOnceAsync(_s.Host.Clock.GetUtcNow(), CancellationToken.None);
    private async Task<Work> Work(Guid id) => await WorkTestKit.Load(_s.Host, id);
    private bool AnyVariant(Guid w) => Variants(w).Any(_s.Host.Blobs.Exists);
    private void FailBlobDeletes(bool on) => _s.Host.Blobs.FailDeletes = on ? p => p.StartsWith("works/", StringComparison.Ordinal) : null;

    [Fact]
    public async Task Admin_remove_with_a_failing_blob_delete_commits_the_removal_and_the_reconciler_finishes_the_take_down()
    {
        var (_, work) = await _s.Story();
        FailBlobDeletes(true);

        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.RemoveWorkAsync(_admin, work, "bad")));

        var w = await Work(work);
        Assert.Equal(WorkStatus.Removed, w.Status);
        Assert.Null(w.TakedownDoneAt);
        Assert.True(AnyVariant(work));
        Assert.Equal(0, await Reconcile()); // still failing: stays owed
        Assert.Null((await Work(work)).TakedownDoneAt);

        FailBlobDeletes(false);
        Assert.Equal(1, await Reconcile());
        Assert.False(AnyVariant(work));
        Assert.NotNull((await Work(work)).TakedownDoneAt);
        Assert.Contains($"/s/{work}", _s.Cdn.Purged);
    }

    [Fact]
    public async Task A_successful_take_down_stamps_the_work_and_reruns_do_nothing()
    {
        var (_, work) = await _s.Story();
        await _s.Mod(m => m.RemoveWorkAsync(_admin, work, "bad"));
        Assert.NotNull((await Work(work)).TakedownDoneAt);
        var purges = _s.Cdn.Purged.Count;
        Assert.Equal(0, await Reconcile());
        Assert.Equal(0, await Reconcile());
        Assert.Equal(purges, _s.Cdn.Purged.Count);
    }

    [Fact]
    public async Task Auto_block_with_a_failing_blob_delete_stays_blocked_and_is_retried()
    {
        FailBlobDeletes(true);
        var (_, work) = await _s.BlockedStory();
        var w = await Work(work);
        Assert.Equal(WorkStatus.Blocked, w.Status);
        Assert.Null(w.TakedownDoneAt);
        Assert.True(AnyVariant(work));

        FailBlobDeletes(false);
        Assert.Equal(1, await Reconcile());
        Assert.False(AnyVariant(work));
        Assert.NotNull((await Work(work)).TakedownDoneAt);
    }

    [Fact]
    public async Task A_failing_cdn_purge_leaves_the_take_down_owed_until_a_retry_succeeds()
    {
        var (_, work) = await _s.Story();
        _s.Cdn.OnPurge = () => throw new IOException("cdn down");
        await _s.Mod(m => m.RemoveWorkAsync(_admin, work, "bad"));
        Assert.False(AnyVariant(work));
        Assert.Null((await Work(work)).TakedownDoneAt);

        _s.Cdn.OnPurge = null;
        Assert.Equal(1, await Reconcile());
        Assert.NotNull((await Work(work)).TakedownDoneAt);
        Assert.Contains($"/s/{work}", _s.Cdn.Purged);
    }

    [Fact]
    public async Task A_published_work_is_never_touched_by_the_reconciler()
    {
        var (_, work) = await _s.Story();
        Assert.Equal(WorkStatus.Published, (await Work(work)).Status);
        Assert.Equal(0, await Reconcile());
        Assert.All(Variants(work), v => Assert.True(_s.Host.Blobs.Exists(v)));
    }

    [Fact]
    public async Task Restore_failing_while_regenerating_leaves_it_blocked_with_no_public_variants()
    {
        var (_, work) = await _s.BlockedStory();
        var flag = await _s.AutoFlagId("work", work);
        _s.Host.Blobs.FailWrites = n => n.StartsWith("works/", StringComparison.Ordinal);

        await Assert.ThrowsAnyAsync<Exception>(() => _s.Mod(m => m.RestoreAsync(_admin, flag, "fp")));

        _s.Host.Blobs.FailWrites = null;
        Assert.Equal(WorkStatus.Blocked, (await Work(work)).Status);
        await Reconcile();
        Assert.False(AnyVariant(work));
        Assert.NotNull((await Work(work)).TakedownDoneAt);
        Assert.Equal("pending", (await _s.LoadReport(flag)).Status);
    }

    [Fact]
    public async Task Restore_failing_at_commit_time_takes_the_regenerated_variants_down_again()
    {
        var (_, work) = await _s.BlockedStory();
        var flag = await _s.AutoFlagId("work", work);
        ToggleFailingHandler.Fail = true;

        await Assert.ThrowsAnyAsync<Exception>(() => _s.Mod(m => m.RestoreAsync(_admin, flag, "fp")));

        ToggleFailingHandler.Fail = false;
        var w = await Work(work);
        Assert.Equal(WorkStatus.Blocked, w.Status);
        Assert.False(w.ModerationOverride);
        await Reconcile();
        Assert.False(AnyVariant(work));
        Assert.NotNull((await Work(work)).TakedownDoneAt);

        // And a clean retry works.
        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.RestoreAsync(_admin, flag, "fp")));
        Assert.Equal(WorkStatus.Published, (await Work(work)).Status);
        Assert.Null((await Work(work)).TakedownDoneAt);
        Assert.All(Variants(work), v => Assert.True(_s.Host.Blobs.Exists(v)));
    }

    [Fact]
    public async Task A_crash_after_regeneration_is_cleaned_up_by_the_reconciler()
    {
        var (_, work) = await _s.BlockedStory();
        // What a crash between regenerating and committing leaves behind: variants present, take-down marked owed.
        using (var scope = _s.Host.Scope())
            await scope.ServiceProvider.GetRequiredService<IWorkVariantLifecycle>().RestoreAsync(work);
        await WorkTestKit.Modify(_s.Host, work, w => w.TakedownDoneAt = null);
        Assert.True(AnyVariant(work));

        Assert.Equal(1, await Reconcile());
        Assert.False(AnyVariant(work));
    }

    [Fact]
    public async Task Restore_with_a_missing_original_reports_unavailable_and_changes_no_state()
    {
        var (_, work) = await _s.BlockedStory();
        var flag = await _s.AutoFlagId("work", work);
        await _s.Host.Blobs.DeletePrefixAsync($"originals/works/{work}");

        Assert.Equal(ModerationOutcome.VariantsUnavailable, await _s.Mod(m => m.RestoreAsync(_admin, flag, "x")));

        var w = await Work(work);
        Assert.Equal(WorkStatus.Blocked, w.Status);
        Assert.False(AnyVariant(work));
        Assert.Equal("pending", (await _s.LoadReport(flag)).Status);
    }

    [Fact]
    public async Task A_reconciler_take_down_that_lands_mid_restore_does_not_leave_a_published_work_without_images()
    {
        var (_, work) = await _s.BlockedStory();
        var flag = await _s.AutoFlagId("work", work);
        var ran = false;
        // While the last variant is being written, a reconciler sweep deletes the earlier ones and stamps the work.
        _s.Host.Blobs.FailWrites = name =>
        {
            if (!ran && name.EndsWith("full.webp", StringComparison.Ordinal))
            {
                ran = true;
                Reconcile().GetAwaiter().GetResult();
            }
            return false;
        };

        Assert.Equal(ModerationOutcome.Ok, await _s.Mod(m => m.RestoreAsync(_admin, flag, "fp")));

        Assert.True(ran);
        Assert.Equal(WorkStatus.Published, (await Work(work)).Status);
        Assert.All(Variants(work), v => Assert.True(_s.Host.Blobs.Exists(v), v));
    }
}
