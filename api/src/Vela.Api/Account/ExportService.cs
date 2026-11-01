using Microsoft.EntityFrameworkCore;
using Vela.Api.Common;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Images;
using Vela.Api.Social;

namespace Vela.Api.Account;

/// <summary>What the contract calls ExportJob. <c>running</c> is shown as <c>pending</c>; a ready export past its expiry is <c>expired</c>.</summary>
public sealed record ExportStatus(Guid Id, string Status, string? DownloadUrl, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);

public interface IExportService
{
    Task<Guid> RequestAsync(Guid userId);
    Task<ExportStatus> GetAsync(Guid userId, Guid exportId);
}

public static class ExportStates
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Ready = "ready";
    public const string Failed = "failed";
    public const string Expired = "expired";
}

public sealed class ExportService(VelaDbContext db, IBlobStore blobs, TimeProvider clock) : IExportService
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    public static readonly TimeSpan DownloadLinkLifetime = TimeSpan.FromMinutes(15);

    public static SafetyException NotFound() => new(StatusCodes.Status404NotFound, "export_not_found", "No such export.");
    private static SafetyException Deleted() => new(StatusCodes.Status403Forbidden, "account_deleted", "This account has been deleted.");

    public async Task<Guid> RequestAsync(Guid userId)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended('export:' || {userId.ToString()}, 0))");
        await EnsureLiveAsync(userId);

        // A failed export does not use up the day, so the user can try again right away.
        var since = now - Window;
        var hits = await db.ExportJobs.AsNoTracking()
            .Where(j => j.UserId == userId && j.Status != ExportStates.Failed && j.RequestedAt > since).Select(j => j.RequestedAt).ToListAsync();
        if (RateLimiting.RetryAfter(hits, 1, Window, now) is { } wait)
            throw new SafetyException(StatusCodes.Status429TooManyRequests, "rate_limited", "You can request one export per day.", wait);

        var job = new ExportJob { Id = Guid.NewGuid(), UserId = userId, Status = ExportStates.Pending, RequestedAt = now };
        db.ExportJobs.Add(job);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return job.Id;
    }

    public async Task<ExportStatus> GetAsync(Guid userId, Guid exportId)
    {
        await EnsureLiveAsync(userId);
        var job = await db.ExportJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == exportId && j.UserId == userId) ?? throw NotFound();
        var now = clock.GetUtcNow();
        var expired = job.Status == ExportStates.Expired || (job.Status == ExportStates.Ready && job.ExpiresAt <= now);
        var status = expired ? ExportStates.Expired : job.Status == ExportStates.Running ? ExportStates.Pending : job.Status;
        var url = status == ExportStates.Ready && job.ArchiveBlob is { } blob ? blobs.CreateReadUrl(blob, DownloadLinkLifetime).ToString() : null;
        return new ExportStatus(job.Id, status, url, job.RequestedAt, job.ExpiresAt);
    }

    private async Task EnsureLiveAsync(Guid userId)
    {
        var deletedAt = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.DeletedAt }).SingleOrDefaultAsync();
        if (deletedAt is null || deletedAt.DeletedAt is not null) throw Deleted();
    }
}
