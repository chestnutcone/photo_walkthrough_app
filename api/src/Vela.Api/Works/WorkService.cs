using Microsoft.EntityFrameworkCore;
using Vela.Api.Admin;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Images;

namespace Vela.Api.Works;

public sealed class WorkService(
    VelaDbContext db, WorkReader reader, IBlobStore blobs, IVariantGenerator variants, WorkOriginals originals, UnderReview review, TimeProvider clock,
    Moderation.MalwareCheck malware, Social.ReportService reports, ILogger<WorkService>? log = null) : IWorkService
{
    public Task<WorkDto?> GetAsync(Guid workId, Guid? viewerId) => reader.GetAsync(workId, viewerId);

    public async Task<WorkDto> UpdateAsync(Guid userId, Guid workId, UpdateWorkRequest req)
    {
        req = WorkValidator.Validate(req);
        await using var tx = await db.Database.BeginTransactionAsync();
        var work = await LockOwnedAsync(userId, workId);
        if (work.Status != WorkStatus.Draft) throw NotEditable();

        work.Title = req.Title!;
        work.Intro = req.Intro ?? "";
        work.AltText = req.AltText;
        await db.Chapters.Where(c => c.WorkId == workId).ExecuteDeleteAsync();
        db.Chapters.AddRange(req.Chapters!.Select((c, i) => new Chapter
        {
            WorkId = workId, Index = i, X = c!.X!.Value, Y = c.Y!.Value, Zoom = c.Zoom,
            Title = c.Title!, Body = c.Body ?? "", Meta = c.Meta, AltText = c.AltText,
        }));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return (await reader.GetAsync(workId, userId))!;
    }

    /// <summary>How long a publish request owns a draft. Far longer than a decode; a crashed request blocks the draft at most this long.</summary>
    public static readonly TimeSpan PublishClaimLease = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Three steps, so no row lock or transaction is held while the image is read, decoded and written (S1b-1):
    /// a short transaction that validates and claims the draft, the image work under the decode gate, then a short transaction
    /// that moves the draft to pending only if the claim is still ours. Any failure releases the claim.
    /// </summary>
    public async Task<WorkDto> RequestPublishAsync(Guid userId, Guid workId)
    {
        var claim = await ClaimForPublishAsync(userId, workId);
        SealedOriginal? seal = null;
        try
        {
            await EnsureOriginalExistsAsync(claim.Source);
            ImageVariants made;
            try
            {
                // S1b-3: copy the upload to a server-owned name once; the variants and every later reader use only that copy.
                seal = await originals.SealAsync(workId);
                // S2-1: the verdict on the sealed copy decides before anything decodes the file.
                if (await malware.AwaitVerdictAsync(workId, seal.Blob) == Moderation.MalwareVerdict.Malicious)
                {
                    await CompletePublishAsync(userId, workId, claim.Until, null, seal);
                    await reports.CreateAutoFlagAsync("work", workId, Moderation.MalwareCheck.Category, Moderation.MalwareCheck.Severity);
                    Telemetry.VelaMetrics.Takedown("malware_block");
                    await DeleteQuietlyAsync(BlobNames.WorkOriginal(workId));
                    return (await reader.GetAsync(workId, userId))!;
                }
                made = await variants.GenerateAsync(seal.Blob, workId);
            }
            catch (InvalidImageException e)
            {
                // The upload cannot be used: remove it so the same bytes cannot be decoded again for free.
                await DeleteQuietlyAsync(BlobNames.WorkOriginal(workId));
                throw new WorkException(StatusCodes.Status400BadRequest, "invalid_image", e.Reason == ImageSniffer.MismatchReason
                    ? "The uploaded file is not the type of image it was sent as." : "The uploaded image could not be used.");
            }
            await CompletePublishAsync(userId, workId, claim.Until, made, seal);
        }
        catch
        {
            // A failing commit can still have committed (review F6): delete our sealed copy only once a fresh connection shows
            // the row does not record it. When that cannot be checked, keep it (an orphan costs storage; a missing seal blocks).
            if (seal is not null && !await SealRecordedAsync(workId, seal.Blob)) await DeleteQuietlyAsync(seal.Blob);
            await ReleaseClaimAsync(workId, claim.Until);
            throw;
        }
        // The writable upload is no longer needed; the publish sweep deletes it again if this fails or the URL re-creates it.
        await DeleteQuietlyAsync(BlobNames.WorkOriginal(workId));
        return (await reader.GetAsync(workId, userId))!;
    }

    private sealed record PublishClaim(string Source, DateTimeOffset Until);

    private async Task<PublishClaim> ClaimForPublishAsync(Guid userId, Guid workId)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync();
        var work = await LockOwnedAsync(userId, workId);
        if (work.Status != WorkStatus.Draft) throw NotEditable();
        if (work.PublishClaimedUntil is { } held && held > now)
            throw new WorkException(StatusCodes.Status409Conflict, "not_editable", "This story is already being published.");
        if (work.ExpiresAt is { } expires && expires <= now)
            throw new WorkException(StatusCodes.Status409Conflict, "draft_expired", "This draft has expired. Start a new story.");
        if (!await db.Chapters.AnyAsync(c => c.WorkId == workId))
            throw new WorkException(StatusCodes.Status400BadRequest, "no_chapters", "Add at least one chapter before publishing.");
        if (string.IsNullOrWhiteSpace(work.Title)) throw WorkException.Validation("title", "is required.");
        // Postgres keeps microseconds; truncating makes the stored value compare equal when it is read back.
        var until = now + PublishClaimLease;
        until = until.AddTicks(-(until.Ticks % 10));
        work.PublishClaimedUntil = until;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new PublishClaim(work.ImageBlob, until);
    }

    /// <summary>Moves the claimed draft to pending, or to blocked when <paramref name="made"/> is null (malware: nothing was decoded).</summary>
    private async Task CompletePublishAsync(Guid userId, Guid workId, DateTimeOffset until, ImageVariants? made, SealedOriginal seal)
    {
        var now = clock.GetUtcNow();
        // The claim step left this work tracked; without clearing, EF would hand back that stale instance instead of the row as
        // it is now, and the re-check below would never see a concurrent take-over, delete or expiry (found by the F7 test).
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync();
        var work = (await db.Works.FromSqlInterpolated($"SELECT * FROM works WHERE id = {workId} AND artist_id = {userId} FOR UPDATE").ToListAsync())
            .SingleOrDefault();
        if (work is null || work.Status != WorkStatus.Draft || work.PublishClaimedUntil != until)
        {
            // Deleted or expired meanwhile: our variants must not stay public. If another request has taken the draft over (our
            // claim lapsed), the variants at this prefix may be its own, so they are left to it.
            await tx.RollbackAsync();
            if (work is null || work is { Status: WorkStatus.Draft, PublishClaimedUntil: null })
                await DeleteQuietlyAsync(BlobNames.WorkVariantsPrefix(workId));
            throw work is null ? WorkException.NotFound() : NotEditable();
        }
        work.SealedOriginalBlob = seal.Blob;
        work.SealedOriginalSha256 = seal.Sha256;
        work.ExpiresAt = null;
        if (made is null)
        {
            work.Status = WorkStatus.Blocked;
            work.TakedownDoneAt = now; // nothing public was ever made
        }
        else
        {
            work.Width = made.Width;
            work.Height = made.Height;
            work.Status = WorkStatus.Pending;
            work.PendingSince = now;
        }
        work.PublishClaimedUntil = null;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private async Task ReleaseClaimAsync(Guid workId, DateTimeOffset until)
    {
        db.ChangeTracker.Clear();
        await db.Works.Where(w => w.Id == workId && w.PublishClaimedUntil == until)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.PublishClaimedUntil, (DateTimeOffset?)null));
    }

    private async Task<bool> SealRecordedAsync(Guid workId, string sealedBlob)
    {
        try
        {
            await using var conn = new Npgsql.NpgsqlConnection(db.Database.GetConnectionString());
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand("SELECT sealed_original_blob FROM works WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", workId);
            return await cmd.ExecuteScalarAsync() is string recorded && recorded == sealedBlob;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log?.LogWarning("Could not confirm whether a failed publish committed ({Reason}); keeping its sealed copy.", e.GetType().Name);
            return true;
        }
    }

    private async Task DeleteQuietlyAsync(string prefix)
    {
        try { await blobs.DeletePrefixAsync(prefix); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log?.LogWarning("Could not delete blobs after a failed publish ({Reason}).", e.GetType().Name);
        }
    }

    /// <summary>
    /// Under the row lock, so a scan or report cannot change the work between the check and the delete. Content under review
    /// (S1b-2) is refused with 409 <c>under_review</c> and nothing changes. Blobs go before the row: a failure rolls back and
    /// leaves the row, so a retry cleans up instead of orphaning files.
    /// </summary>
    public async Task DeleteAsync(Guid userId, Guid workId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var work = await LockOwnedAsync(userId, workId);
        if (await review.WorkAsync(work))
            throw new WorkException(StatusCodes.Status409Conflict, UnderReview.Code,
                "This story is being reviewed by our moderators and can't be deleted until the review is finished.");
        foreach (var prefix in BlobNames.WorkPrefixes(workId)) await blobs.DeletePrefixAsync(prefix);
        await db.Works.Where(w => w.Id == workId && w.ArtistId == userId).ExecuteDeleteAsync();
        await tx.CommitAsync();
    }

    /// <summary>Loads the caller's own work under a row lock; anything else is reported as not found.</summary>
    private async Task<Work> LockOwnedAsync(Guid userId, Guid workId) =>
        (await db.Works.FromSqlInterpolated($"SELECT * FROM works WHERE id = {workId} AND artist_id = {userId} FOR UPDATE").ToListAsync())
            .SingleOrDefault() ?? throw WorkException.NotFound();

    private async Task EnsureOriginalExistsAsync(string blob)
    {
        try { await (await blobs.OpenReadAsync(blob)).DisposeAsync(); }
        catch (FileNotFoundException)
        {
            throw new WorkException(StatusCodes.Status400BadRequest, "image_missing", "Upload the image before publishing.");
        }
    }

    private static WorkException NotEditable() =>
        new(StatusCodes.Status409Conflict, "not_editable", "Only drafts can be changed.");
}
