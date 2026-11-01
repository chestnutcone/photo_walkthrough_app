using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;

namespace Vela.Api.Account;

/// <summary>
/// Every minute: claims accounts marked deleted and runs <see cref="DeletionJob"/> for each. Claiming is a short transaction
/// (<c>FOR UPDATE SKIP LOCKED</c>) that sets a lease and commits, so parallel instances never take the same account and no row
/// lock or transaction is held while blobs, CDN and Apple are handled. A failure is counted and the account is skipped for
/// <see cref="Backoff"/>, so one poison account cannot starve the others. A crashed run is retried when its lease expires.
/// </summary>
public sealed class DeletionWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<DeletionWorker> log) : BackgroundService
{
    public const int BatchSize = 5;
    public static readonly TimeSpan Backoff = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);
    /// <summary>How often a deferred account (content under review, S1b-2) is looked at again; never later than its 30-day cap.</summary>
    public static readonly TimeSpan DeferredRecheck = TimeSpan.FromHours(1);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try { await RunOnceAsync(clock.GetUtcNow(), stoppingToken); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError("Account deletion sweep failed ({Reason}).", e.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Returns how many accounts were hard-deleted by this sweep.</summary>
    public async Task<int> RunOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        var done = 0;
        foreach (var id in await ClaimAsync(now, ct))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                if (await DeferAsync(scope.ServiceProvider, id, now)) continue;
                await scope.ServiceProvider.GetRequiredService<DeletionJob>().RunAsync(id, ct);
                done++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning("Account {UserId} hard delete failed ({Reason}); retrying later.", id, e.GetType().Name);
                await RecordFailureAsync(id, now);
            }
        }
        return done;
    }

    /// <summary>
    /// S1b-2: while the account still has content under review (and its 30-day cap has not passed) the hard delete waits. The lease
    /// is reused as the "not before" mark, so the account is claimed again after <see cref="DeferredRecheck"/> or at the cap.
    /// </summary>
    private async Task<bool> DeferAsync(IServiceProvider sp, Guid id, DateTimeOffset now)
    {
        var db = sp.GetRequiredService<VelaDbContext>();
        var status = await DeletionDeferral.EvaluateAsync(db, sp.GetRequiredService<Admin.UnderReview>(), id, now, log);
        if (status.CompletesBy is not { } cap) return false;
        var next = now + DeferredRecheck < cap ? now + DeferredRecheck : cap;
        await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletionLeaseUntil, next));
        return true;
    }

    private async Task<List<Guid>> ClaimAsync(DateTimeOffset now, CancellationToken ct)
    {
        var retryBefore = now - Backoff;
        var leaseUntil = now + Lease;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VelaDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ids = await db.Database.SqlQuery<Guid>(
            $"SELECT id AS \"Value\" FROM users WHERE deleted_at IS NOT NULL AND (deletion_lease_until IS NULL OR deletion_lease_until <= {now}) AND (deletion_failed_at IS NULL OR deletion_failed_at <= {retryBefore}) ORDER BY deleted_at, id LIMIT {BatchSize} FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);
        if (ids.Count > 0)
            await db.Users.Where(u => ids.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletionLeaseUntil, leaseUntil), ct);
        await tx.CommitAsync(ct);
        return ids;
    }

    private async Task RecordFailureAsync(Guid id, DateTimeOffset now)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<VelaDbContext>();
            await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.DeletionFailures, u => u.DeletionFailures + 1).SetProperty(u => u.DeletionFailedAt, now)
                .SetProperty(u => u.DeletionLeaseUntil, (DateTimeOffset?)null));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError("Could not record a deletion failure for {UserId} ({Reason}).", id, e.GetType().Name);
        }
    }
}
