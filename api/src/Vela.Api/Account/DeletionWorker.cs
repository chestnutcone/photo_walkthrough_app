using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;

namespace Vela.Api.Account;

/// <summary>
/// Every minute: finds accounts marked deleted that still exist and runs <see cref="DeletionJob"/> for a few of them.
/// A row lock (<c>FOR UPDATE SKIP LOCKED</c>) held for the duration makes parallel instances safe. A failure is counted and the
/// account is skipped for <see cref="Backoff"/>, so one poison account cannot starve the others.
/// </summary>
public sealed class DeletionWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<DeletionWorker> log) : BackgroundService
{
    public const int BatchSize = 5;
    public static readonly TimeSpan Backoff = TimeSpan.FromMinutes(5);
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

    /// <summary>Returns how many accounts were hard-deleted in this sweep.</summary>
    public async Task<int> RunOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        var retryBefore = now - Backoff;
        List<Guid> candidates;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VelaDbContext>();
            candidates = await db.Users.AsNoTracking()
                .Where(u => u.DeletedAt != null && (u.DeletionFailedAt == null || u.DeletionFailedAt <= retryBefore))
                .OrderBy(u => u.DeletedAt).ThenBy(u => u.Id).Take(BatchSize).Select(u => u.Id).ToListAsync(ct);
        }

        var done = 0;
        foreach (var id in candidates)
        {
            try { if (await ProcessAsync(id, now, retryBefore, ct)) done++; }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning("Account {UserId} hard delete failed ({Reason}); retrying later.", id, e.GetType().Name);
                await RecordFailureAsync(id, now);
            }
        }
        return done;
    }

    private async Task<bool> ProcessAsync(Guid id, DateTimeOffset now, DateTimeOffset retryBefore, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VelaDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var claimed = await db.Database.SqlQuery<Guid>(
            $"SELECT id AS \"Value\" FROM users WHERE id = {id} AND deleted_at IS NOT NULL AND (deletion_failed_at IS NULL OR deletion_failed_at <= {retryBefore}) FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);
        if (claimed.Count == 0) return false; // another instance has it, or it is already gone

        await scope.ServiceProvider.GetRequiredService<DeletionJob>().RunAsync(id, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private async Task RecordFailureAsync(Guid id, DateTimeOffset now)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<VelaDbContext>();
            await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.DeletionFailures, u => u.DeletionFailures + 1).SetProperty(u => u.DeletionFailedAt, now));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError("Could not record a deletion failure for {UserId} ({Reason}).", id, e.GetType().Name);
        }
    }
}
