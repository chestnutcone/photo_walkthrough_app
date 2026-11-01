using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Images;

namespace Vela.Api.Admin;

/// <summary>
/// Deletes evidence holds whose <c>HoldUntil</c> has passed: blobs first, then the row and an audit entry, so a failed
/// blob delete leaves the row and the next run retries. Logs exception type names only.
/// </summary>
public sealed class EvidenceRetentionJob(IServiceScopeFactory scopes, TimeProvider clock, ILogger<EvidenceRetentionJob> log) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    public const int MaxPerRun = 100;
    /// <summary>Audit actor for automatic actions.</summary>
    public static readonly Guid SystemActor = Guid.Empty;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var removed = await RunOnceAsync(clock.GetUtcNow(), stoppingToken);
                if (removed > 0) log.LogInformation("Evidence retention removed {Count} expired holds.", removed);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError("Evidence retention run failed ({Reason}).", e.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Returns how many holds were fully deleted.</summary>
    public async Task<int> RunOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VelaDbContext>();
        var blobs = scope.ServiceProvider.GetRequiredService<IBlobStore>();
        var due = await db.EvidenceHolds.AsNoTracking().Where(h => h.HoldUntil <= now).OrderBy(h => h.HoldUntil).Take(MaxPerRun)
            .Select(h => new { h.Id, h.BlobPrefix }).ToListAsync(ct);
        var removed = 0;
        foreach (var hold in due)
        {
            try
            {
                // Lock the row and re-check its date first: an NCMEC submission extends HoldUntil under the same lock,
                // so a hold that was just extended is skipped and keeps its blobs. Blobs go before the row, so a failure retries.
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var due1 = await db.Database.SqlQuery<Guid>(
                    $"SELECT id AS \"Value\" FROM evidence_holds WHERE id = {hold.Id} AND hold_until <= {now} FOR UPDATE").ToListAsync(ct);
                if (due1.Count == 0) continue;
                await blobs.DeletePrefixAsync(hold.BlobPrefix);
                await db.EvidenceHolds.Where(h => h.Id == hold.Id).ExecuteDeleteAsync(ct);
                db.AdminActions.Add(new AdminAction
                {
                    Id = Guid.NewGuid(), AdminId = SystemActor, Action = "evidence_expired", TargetType = "evidence", TargetId = hold.Id,
                    Reason = "retention period ended", At = now,
                });
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                removed++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                log.LogWarning("Could not remove an expired evidence hold ({Reason}); it will be retried.", e.GetType().Name);
            }
        }
        return removed;
    }
}
