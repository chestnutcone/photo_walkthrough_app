using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Vela.Api.Common;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Images;

namespace Vela.Api.Admin;

/// <summary>
/// Creates evidence holds. Order: copy the blobs, then commit the row and its audit entry together; if either fails nothing
/// is left behind (partial copies are deleted) and the caller takes nothing down.
/// </summary>
public sealed partial class EvidenceService(
    VelaDbContext db, IBlobStore blobs, TimeProvider clock, INcmecReporter reporter, EvidenceSnapshotBuilder snapshots, ILogger<EvidenceService> log)
    : IEvidenceService
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(365);
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] VariantNames = ["thumb", "feed", "full"];

    public async Task<EvidenceCapture> CaptureAsync(Guid adminId, Report report, string reason)
    {
        var target = await ResolveAsync(report);
        if (target is null) return new EvidenceCapture(ModerationOutcome.NotFound, null);
        if (await FindAsync(report.TargetType, report.TargetId) is { } existing) return new EvidenceCapture(ModerationOutcome.Ok, existing);

        var holdId = Guid.NewGuid();
        List<string> files;
        try { files = await CopyBlobsAsync(holdId, target.Work?.Id); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError("Copying evidence for a {TargetType} failed ({Reason}); nothing was taken down.", report.TargetType, e.GetType().Name);
            await DiscardCopiesAsync(holdId);
            return new EvidenceCapture(ModerationOutcome.EvidenceCopyFailed, null);
        }

        try
        {
            var snapshot = await snapshots.BuildAsync(report.TargetType, target.OwnerId, target.Work, target.Reply, files.Contains("original"), files);
            var now = clock.GetUtcNow();
            var hold = new EvidenceHold
            {
                Id = holdId, UserId = target.OwnerId, WorkId = target.Work?.Id, ReplyId = target.Reply?.Id, TargetType = report.TargetType,
                BlobPrefix = BlobNames.EvidencePrefix(holdId), Reason = reason, HoldUntil = now + Retention,
                Snapshot = JsonSerializer.Serialize(snapshot, Json), CreatedByAdminId = adminId, CreatedAt = now,
            };
            await using var tx = await db.Database.BeginTransactionAsync();
            db.EvidenceHolds.Add(hold);
            db.AdminActions.Add(new AdminAction
            {
                Id = Guid.NewGuid(), AdminId = adminId, Action = "mark_illegal", TargetType = report.TargetType, TargetId = report.TargetId,
                Reason = reason, At = now,
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return new EvidenceCapture(ModerationOutcome.Ok, hold);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent mark of the same target won: use its hold and drop our copy.
            db.ChangeTracker.Clear();
            await DiscardCopiesAsync(holdId);
            return new EvidenceCapture(ModerationOutcome.Ok, await FindAsync(report.TargetType, report.TargetId));
        }
        catch
        {
            await DiscardCopiesAsync(holdId);
            throw;
        }
    }

    private sealed record Target(Guid OwnerId, Work? Work, Reply? Reply);

    private async Task<Target?> ResolveAsync(Report report)
    {
        switch (report.TargetType)
        {
            case "work":
                var work = await db.Works.AsNoTracking().SingleOrDefaultAsync(w => w.Id == report.TargetId);
                return work is null ? null : new Target(work.ArtistId, work, null);
            case "reply":
                var reply = await db.Replies.AsNoTracking().SingleOrDefaultAsync(r => r.Id == report.TargetId);
                return reply is null ? null : new Target(reply.AuthorId, null, reply);
            case "user":
                return await db.Users.AsNoTracking().AnyAsync(u => u.Id == report.TargetId) ? new Target(report.TargetId, null, null) : null;
            default:
                return null;
        }
    }

    public Task<EvidenceHold?> FindAsync(string targetType, Guid targetId) => targetType switch
    {
        "work" => db.EvidenceHolds.AsNoTracking().FirstOrDefaultAsync(h => h.WorkId == targetId),
        "reply" => db.EvidenceHolds.AsNoTracking().FirstOrDefaultAsync(h => h.ReplyId == targetId),
        _ => db.EvidenceHolds.AsNoTracking().FirstOrDefaultAsync(h => h.TargetType == "user" && h.UserId == targetId),
    };

    /// <summary>Copies the private original and any public variants still present. A missing source is skipped; any other failure throws.</summary>
    private async Task<List<string>> CopyBlobsAsync(Guid holdId, Guid? workId)
    {
        var files = new List<string>();
        if (workId is not { } id) return files;
        if (await CopyAsync(BlobNames.WorkOriginal(id), BlobNames.EvidenceOriginal(holdId), "application/octet-stream")) files.Add("original");
        foreach (var name in VariantNames)
            if (await CopyAsync($"{BlobNames.WorkVariantsPrefix(id)}{name}.webp", BlobNames.EvidenceVariant(holdId, name), "image/webp"))
                files.Add($"variants/{name}.webp");
        return files;
    }

    private async Task<bool> CopyAsync(string from, string to, string contentType)
    {
        Stream source;
        try { source = await blobs.OpenReadAsync(from); }
        catch (FileNotFoundException) { return false; }
        await using (source) await blobs.WriteAsync(to, source, contentType);
        return true;
    }

    private async Task DiscardCopiesAsync(Guid holdId)
    {
        try { await blobs.DeletePrefixAsync(BlobNames.EvidencePrefix(holdId)); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning("Could not remove a partial evidence copy ({Reason}).", e.GetType().Name);
        }
    }
}
