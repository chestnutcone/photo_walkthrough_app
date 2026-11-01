using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vela.Api.Auth;
using Vela.Api.Branding;
using Vela.Api.Common;
using Vela.Api.Data;
using Vela.Api.Images;

namespace Vela.Api.Account;

/// <summary>
/// Builds pending exports (claimed with <c>FOR UPDATE SKIP LOCKED</c>, so parallel instances never build one twice) and
/// deletes archives past their 7-day life. A job stuck in running is reclaimed after <see cref="StuckAfter"/>.
/// </summary>
public sealed class ExportWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ExportWorker> log) : BackgroundService
{
    public const int MaxJobsPerRun = 5;
    public const int MaxExpiriesPerRun = 100;
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try { await RunOnceAsync(clock.GetUtcNow(), stoppingToken); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError("Export worker run failed ({Reason}).", e.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        await ExpireOldAsync(now, ct);
        for (var i = 0; i < MaxJobsPerRun; i++)
            if (await BuildNextAsync(now, ct) is false) break;
    }

    private async Task ExpireOldAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VelaDbContext>();
        var blobs = scope.ServiceProvider.GetRequiredService<IBlobStore>();
        var due = await db.ExportJobs.AsNoTracking().Where(j => j.Status == ExportStates.Ready && j.ExpiresAt <= now)
            .OrderBy(j => j.ExpiresAt).Take(MaxExpiriesPerRun).Select(j => new { j.Id, j.ArchiveBlob }).ToListAsync(ct);
        foreach (var job in due)
        {
            try
            {
                if (job.ArchiveBlob is { } blob) await blobs.DeletePrefixAsync(blob); // blob first: a failure retries next run
                await db.ExportJobs.Where(j => j.Id == job.Id && j.Status == ExportStates.Ready)
                    .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, ExportStates.Expired), ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning("Could not expire export {ExportId} ({Reason}).", job.Id, e.GetType().Name);
            }
        }
    }

    /// <summary>Returns false when nothing was waiting.</summary>
    private async Task<bool> BuildNextAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<VelaDbContext>();
        var claim = await ClaimAsync(db, now, ct);
        if (claim is null) return false;

        var (jobId, userId) = claim.Value;
        var blobName = BlobNames.ExportArchive(userId, jobId);
        var blobs = sp.GetRequiredService<IBlobStore>();
        try
        {
            if (!await db.Users.AnyAsync(u => u.Id == userId && u.DeletedAt == null, ct)) throw new InvalidOperationException("account_unavailable");
            await sp.GetRequiredService<ExportArchiveBuilder>().BuildAsync(userId, blobName, ct);
            var ready = await db.ExportJobs.Where(j => j.Id == jobId && j.Status == ExportStates.Running).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, ExportStates.Ready).SetProperty(j => j.CompletedAt, now)
                .SetProperty(j => j.ExpiresAt, now + ExportService.Retention).SetProperty(j => j.ArchiveBlob, blobName), ct);
            if (ready == 0) { await blobs.DeletePrefixAsync(blobName); return true; } // the account was deleted meanwhile
            await NotifyAsync(sp, db, userId);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning("Export {ExportId} failed ({Reason}).", jobId, e.GetType().Name);
            await FailAsync(db, blobs, jobId, blobName, now);
        }
        return true;
    }

    private static async Task<(Guid JobId, Guid UserId)?> ClaimAsync(VelaDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var stuckBefore = now - StuckAfter;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var id = await db.Database.SqlQuery<Guid>(
            $"SELECT id AS \"Value\" FROM export_jobs WHERE status = 'pending' OR (status = 'running' AND started_at < {stuckBefore}) ORDER BY requested_at LIMIT 1 FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);
        if (id.Count == 0) return null;
        var job = await db.ExportJobs.SingleAsync(j => j.Id == id[0], ct);
        job.Status = ExportStates.Running;
        job.StartedAt = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return (job.Id, job.UserId);
    }

    private async Task FailAsync(VelaDbContext db, IBlobStore blobs, Guid jobId, string blobName, DateTimeOffset now)
    {
        try
        {
            await db.ExportJobs.Where(j => j.Id == jobId).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, ExportStates.Failed).SetProperty(j => j.CompletedAt, now));
            await blobs.DeletePrefixAsync(blobName);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError("Could not mark export {ExportId} failed ({Reason}).", jobId, e.GetType().Name);
        }
    }

    /// <summary>Best effort: plain text, no link and nothing the user wrote; the user opens the app to download.</summary>
    private async Task NotifyAsync(IServiceProvider sp, VelaDbContext db, Guid userId)
    {
        try
        {
            var to = await LoginEmail.ForUserAsync(db, userId);
            if (to is null) { log.LogInformation("Export email skipped ({Reason}).", "no_recipient"); return; }
            var app = sp.GetRequiredService<IOptions<BrandingOptions>>().Value.AppName;
            await sp.GetRequiredService<IEmailSender>().SendAsync(to, $"Your {app} data export is ready",
                $"Your {app} data export is ready.\n\nOpen the app to download it from your account settings. The download is available for 7 days.\n");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning("Export email failed ({Reason}).", e.GetType().Name);
        }
    }
}
