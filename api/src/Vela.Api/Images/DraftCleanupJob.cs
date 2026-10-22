using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;
using Vela.Api.Data.Entities;

namespace Vela.Api.Images;

/// <summary>Deletes expired drafts and their blobs every 15 minutes. Published and pending works are never touched.</summary>
public sealed class DraftCleanupJob(IServiceScopeFactory scopes, TimeProvider clock, ILogger<DraftCleanupJob> log) : BackgroundService
{
    public const int BatchSize = 100;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await RunOnceAsync(clock.GetUtcNow(), stoppingToken); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError("Draft cleanup run failed ({Reason}).", e.GetType().Name);
            }
        }
    }

    public async Task RunOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VelaDbContext>();
        var blobs = scope.ServiceProvider.GetRequiredService<IBlobStore>();

        var ids = await db.Works
            .Where(w => w.Status == WorkStatus.Draft && w.ExpiresAt != null && w.ExpiresAt <= now)
            .OrderBy(w => w.ExpiresAt).Take(BatchSize).Select(w => w.Id).ToListAsync(ct);

        var deleted = 0;
        foreach (var id in ids)
        {
            try
            {
                // Blobs first: a failure leaves the row, so the next run retries instead of orphaning files.
                await blobs.DeletePrefixAsync(BlobNames.WorkVariantsPrefix(id));
                await blobs.DeletePrefixAsync(BlobNames.WorkOriginal(id));
                deleted += await db.Works.Where(w => w.Id == id && w.Status == WorkStatus.Draft && w.ExpiresAt <= now).ExecuteDeleteAsync(ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning("Could not clean up an expired draft ({Reason}).", e.GetType().Name);
            }
        }
        if (ids.Count > 0) log.LogInformation("Draft cleanup removed {Count} of {Found} expired drafts.", deleted, ids.Count);
    }
}
