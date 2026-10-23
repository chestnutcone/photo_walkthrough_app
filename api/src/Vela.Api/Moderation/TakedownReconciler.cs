using Microsoft.EntityFrameworkCore;
using Vela.Api.Admin;
using Vela.Api.Data;

namespace Vela.Api.Moderation;

/// <summary>
/// Finishes take-downs that did not complete: blocked or removed works whose <c>TakedownDoneAt</c> is still null get their
/// public variants deleted and the CDN purged again. Each work is claimed with FOR UPDATE SKIP LOCKED, so instances never
/// collide and a restore (which locks the same row) waits for a running take-down.
/// </summary>
public sealed class TakedownReconciler(IServiceScopeFactory scopes, ILogger<TakedownReconciler> log)
{
    public const int BatchLimit = 20;

    /// <summary>Returns how many works were taken down in this run.</summary>
    public async Task<int> RunOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        List<Guid> candidates;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VelaDbContext>();
            candidates = await db.Works.AsNoTracking()
                .Where(w => (w.Status == Data.Entities.WorkStatus.Blocked || w.Status == Data.Entities.WorkStatus.Removed) && w.TakedownDoneAt == null)
                .OrderBy(w => w.CreatedAt).Select(w => w.Id).Take(BatchLimit).ToListAsync(ct);
        }
        var done = 0;
        foreach (var id in candidates)
            if (await TryTakeDownAsync(id, ct)) done++;
        return done;
    }

    private async Task<bool> TryTakeDownAsync(Guid workId, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<VelaDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var claimed = await db.Database.SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM works
                WHERE id = {workId} AND status IN ('blocked', 'removed') AND takedown_done_at IS NULL
                FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct);
            if (claimed.Count == 0) return false;
            await scope.ServiceProvider.GetRequiredService<IWorkVariantLifecycle>().TakeDownAsync(workId);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError("Take-down retry for work {WorkId} failed ({Reason}); it will be retried.", workId, e.GetType().Name);
            return false;
        }
    }
}
