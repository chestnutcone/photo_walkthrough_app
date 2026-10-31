using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;
using Vela.Api.Data.Entities;

namespace Vela.Api.Legal;

/// <summary>
/// DMCA notices hold third-party personal data (claimant name, email, address, phone, IP) and, for counter-notices, the owner's
/// sworn statement. They are kept past account deletion as legal records (safe harbor, repeat-infringer policy, possible claims),
/// but not forever (FINALAPI I5): a closed notice (rejected, taken down, restored) is deleted once its last step is older than
/// <c>Legal:DmcaRetentionDays</c> (default 3 years; counsel to confirm). Open notices (pending, counter-notice, forwarded) are
/// never touched. Runs daily; safe to run twice.
/// </summary>
public sealed class DmcaRetentionJob(IServiceScopeFactory scopes, TimeProvider clock, IConfiguration config, ILogger<DmcaRetentionJob> log)
    : BackgroundService
{
    public const int DefaultRetentionDays = 3 * 365;
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private static readonly string[] Closed = [DmcaStatus.Rejected, DmcaStatus.Takedown, DmcaStatus.Restored];

    public TimeSpan Retention => TimeSpan.FromDays(Math.Max(1, config.GetValue("Legal:DmcaRetentionDays", DefaultRetentionDays)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try { await RunOnceAsync(clock.GetUtcNow(), stoppingToken); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError("DMCA retention run failed ({Reason}).", e.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Deletes expired closed notices and writes one audit row per notice; returns how many were deleted.</summary>
    public async Task<int> RunOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VelaDbContext>();
        var cutoff = now - Retention;
        // Steps happen in this order, so the latest non-null one is the last activity (a restore follows the forward).
        var due = await db.DmcaNotices.AsNoTracking()
            .Where(n => Closed.Contains(n.Status) && (n.ForwardedAt ?? n.CounterAt ?? n.TakedownAt ?? n.CreatedAt) < cutoff)
            .OrderBy(n => n.CreatedAt).Take(500).Select(n => n.Id).ToListAsync(ct);
        if (due.Count == 0) return 0;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var removed = await db.DmcaNotices.Where(n => due.Contains(n.Id) && Closed.Contains(n.Status)).ExecuteDeleteAsync(ct);
        foreach (var id in due)
            db.AdminActions.Add(new AdminAction
            {
                Id = Guid.NewGuid(), AdminId = Admin.EvidenceRetentionJob.SystemActor, Action = "dmca_expired", TargetType = "dmca_notice",
                TargetId = id, Reason = "retention period ended", At = now,
            });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        log.LogInformation("DMCA retention removed {Count} closed notices.", removed);
        return removed;
    }
}
