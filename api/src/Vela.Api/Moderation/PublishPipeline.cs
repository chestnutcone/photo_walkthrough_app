using Vela.Api.Telemetry;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Common;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Images;
using Vela.Api.Social;

namespace Vela.Api.Moderation;

/// <summary>
/// Moves works from pending to published (or blocked). Each work is claimed with FOR UPDATE SKIP LOCKED in its own
/// transaction, so restarts and several instances never scan or publish the same work twice.
/// </summary>
public sealed class PublishPipeline(IServiceScopeFactory scopes, ILogger<PublishPipeline> log)
{
    public const int MaxTextChars = 9000;
    public const int BlockSeverity = 4;
    public const int MaxScanAttempts = 3;
    public const int BatchLimit = 50;
    public static readonly TimeSpan MinPendingAge = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan RetryGap = TimeSpan.FromMinutes(3);

    private sealed record Verdict(int Severity, string Category);

    /// <summary>Processes the pending works that are due at <paramref name="now"/>; <paramref name="minAge"/> defers brand-new ones.</summary>
    public async Task RunOnceAsync(DateTimeOffset now, CancellationToken ct, TimeSpan? minAge = null)
    {
        for (var i = 0; i < BatchLimit; i++)
            if (!await ProcessNextAsync(now, minAge ?? TimeSpan.Zero, ct)) break;
    }

    /// <summary>
    /// Handles one work. A failure while processing it (for example an event handler throwing) rolls that work back, is counted
    /// on the work, and the sweep moves on, so one poison work cannot starve the others. It is retried after <see cref="RetryGap"/>.
    /// </summary>
    private async Task<bool> ProcessNextAsync(DateTimeOffset now, TimeSpan minAge, CancellationToken ct)
    {
        Guid? claimed = null;
        try { return await ProcessClaimedAsync(now, minAge, id => claimed = id, ct); }
        catch (Exception e) when (e is not OperationCanceledException && claimed is not null)
        {
            log.LogError("Publishing work {WorkId} failed ({Reason}); it will be retried.", claimed, e.GetType().Name);
            await RecordFailureAsync(claimed.Value, now, ct);
            return true;
        }
    }

    private async Task RecordFailureAsync(Guid workId, DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VelaDbContext>();
        await db.Works.Where(w => w.Id == workId).ExecuteUpdateAsync(s => s
            .SetProperty(w => w.PublishFailures, w => w.PublishFailures + 1)
            .SetProperty(w => w.LastPublishFailureAt, now), ct);
    }

    private async Task<bool> ProcessClaimedAsync(DateTimeOffset now, TimeSpan minAge, Action<Guid> onClaim, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<VelaDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var work = await ClaimAsync(db, now, minAge, ct);
        if (work is null) return false;
        onClaim(work.Id);

        var scanner = sp.GetRequiredService<IContentSafety>();
        var blobs = sp.GetRequiredService<IBlobStore>();
        var bus = sp.GetRequiredService<IDomainEventBus>();
        var reports = sp.GetRequiredService<ReportService>();
        var lifecycle = sp.GetRequiredService<Admin.IWorkVariantLifecycle>();

        Verdict? verdict = null;
        Guid? blockedId = null;
        var failed = false;
        if (!work.ModerationOverride)
        {
            try { verdict = await ScanAsync(db, scanner, blobs, work, ct); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failed = true;
                VelaMetrics.ScanFailed("work");
                log.LogWarning("Content scan failed for work {WorkId} ({Reason}).", work.Id, e.GetType().Name);
            }
        }

        if (failed)
        {
            work.ScanAttempts++;
            work.LastScanAttemptAt = now;
            if (work.ScanAttempts < MaxScanAttempts) await db.SaveChangesAsync(ct);
            else
            {
                await PublishAsync(db, bus, work, now, ct);
                await reports.CreateAutoFlagAsync("work", work.Id, "scan_unavailable", 0);
            }
        }
        else if (verdict is { } v && v.Severity >= BlockSeverity)
        {
            work.Status = WorkStatus.Blocked;
            work.TakedownDoneAt = null; // a take-down is owed until it has actually happened
            await db.SaveChangesAsync(ct);
            await reports.CreateAutoFlagAsync("work", work.Id, v.Category, v.Severity);
            blockedId = work.Id;
        }
        else await PublishAsync(db, bus, work, now, ct);

        await tx.CommitAsync(ct);
        // The block is committed first; the images follow. If this fails the reconciler finishes the job.
        if (blockedId is { } blocked)
        {
            VelaMetrics.Takedown("auto_block");
            try { await lifecycle.TakeDownAsync(blocked); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError("Take-down of blocked work {WorkId} failed ({Reason}); it will be retried.", blocked, e.GetType().Name);
            }
        }
        return true;
    }

    private static Task<Work?> ClaimAsync(VelaDbContext db, DateTimeOffset now, TimeSpan minAge, CancellationToken ct)
    {
        var pendingBefore = now - minAge;
        var retryBefore = now - RetryGap;
        return db.Works.FromSqlInterpolated($"""
            SELECT * FROM works
            WHERE status = 'pending' AND pending_since <= {pendingBefore}
              AND (scan_attempts = 0 OR last_scan_attempt_at <= {retryBefore})
              AND (publish_failures = 0 OR last_publish_failure_at <= {retryBefore})
            ORDER BY pending_since
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """).SingleOrDefaultAsync(ct);
    }

    private static async Task PublishAsync(VelaDbContext db, IDomainEventBus bus, Work work, DateTimeOffset now, CancellationToken ct)
    {
        work.Status = WorkStatus.Published;
        work.PublishedAt = now;
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(new WorkPublished(work.Id, work.ArtistId), ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<Verdict> ScanAsync(VelaDbContext db, IContentSafety scanner, IBlobStore blobs, Work work, CancellationToken ct)
    {
        await using (var image = await blobs.OpenReadAsync($"{BlobNames.WorkVariantsPrefix(work.Id)}feed.webp"))
        {
            var severity = await scanner.SeverityAsync(image);
            if (severity >= BlockSeverity) return new Verdict(severity, "unsafe_image");
        }
        var chapters = await db.Chapters.AsNoTracking().Where(c => c.WorkId == work.Id).OrderBy(c => c.Index).ToListAsync(ct);
        var parts = new List<string> { work.Title, work.Intro, work.AltText ?? "" };
        foreach (var c in chapters) parts.AddRange([c.Title, c.Body, c.Meta ?? "", c.AltText ?? ""]);
        var worst = 0;
        foreach (var chunk in Chunk(parts))
            worst = Math.Max(worst, await scanner.SeverityAsync(chunk));
        return new Verdict(worst, "unsafe_text");
    }

    /// <summary>Packs the non-empty parts into requests of at most <see cref="MaxTextChars"/>, splitting oversized parts.</summary>
    public static List<string> Chunk(IEnumerable<string> parts)
    {
        var chunks = new List<string>();
        var sb = new StringBuilder();
        void Flush() { if (sb.Length > 0) chunks.Add(sb.ToString()); sb.Clear(); }
        foreach (var part in parts.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            for (var i = 0; i < part.Length; i += MaxTextChars)
            {
                var piece = part.Substring(i, Math.Min(MaxTextChars, part.Length - i));
                if (sb.Length + 1 + piece.Length > MaxTextChars) Flush();
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(piece);
            }
        }
        Flush();
        return chunks;
    }
}

/// <summary>Sweeps pending works every 30 seconds.</summary>
public sealed class PublishPipelineWorker(PublishPipeline pipeline, TakedownReconciler takedowns, TimeProvider clock, ILogger<PublishPipelineWorker> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try { await pipeline.RunOnceAsync(clock.GetUtcNow(), stoppingToken, PublishPipeline.MinPendingAge); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError("Publish pipeline sweep failed ({Reason}).", e.GetType().Name);
            }
            try { await takedowns.RunOnceAsync(clock.GetUtcNow(), stoppingToken); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError("Take-down reconciliation failed ({Reason}).", e.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
