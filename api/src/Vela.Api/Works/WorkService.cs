using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Images;

namespace Vela.Api.Works;

public sealed class WorkService(VelaDbContext db, WorkReader reader, IBlobStore blobs, IVariantGenerator variants, TimeProvider clock) : IWorkService
{
    public Task<WorkDto?> GetAsync(Guid workId, Guid? viewerId) => reader.GetAsync(workId, viewerId);

    public async Task<WorkDto> UpdateAsync(Guid userId, Guid workId, UpdateWorkRequest req)
    {
        WorkValidator.Validate(req);
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

    public async Task<WorkDto> RequestPublishAsync(Guid userId, Guid workId)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync();
        var work = await LockOwnedAsync(userId, workId);
        if (work.Status != WorkStatus.Draft) throw NotEditable();
        if (work.ExpiresAt is { } expires && expires <= now)
            throw new WorkException(StatusCodes.Status409Conflict, "draft_expired", "This draft has expired. Start a new story.");
        if (!await db.Chapters.AnyAsync(c => c.WorkId == workId))
            throw new WorkException(StatusCodes.Status400BadRequest, "no_chapters", "Add at least one chapter before publishing.");
        if (string.IsNullOrWhiteSpace(work.Title)) throw WorkException.Validation("title", "is required.");
        await EnsureOriginalExistsAsync(work.ImageBlob);

        try
        {
            var made = await variants.GenerateAsync(work.ImageBlob);
            work.Width = made.Width;
            work.Height = made.Height;
        }
        catch (InvalidImageException)
        {
            throw new WorkException(StatusCodes.Status400BadRequest, "invalid_image", "The uploaded image could not be used.");
        }
        work.ExpiresAt = null;
        work.Status = WorkStatus.Pending;
        work.PendingSince = now;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return (await reader.GetAsync(workId, userId))!;
    }

    public async Task DeleteAsync(Guid userId, Guid workId)
    {
        var owner = await db.Works.AsNoTracking().Where(w => w.Id == workId).Select(w => (Guid?)w.ArtistId).FirstOrDefaultAsync();
        if (owner is null) return;
        if (owner != userId) throw WorkException.NotFound();
        // Blobs first: a failure leaves the row, so a retry cleans up instead of orphaning files.
        await blobs.DeletePrefixAsync(BlobNames.WorkVariantsPrefix(workId));
        await blobs.DeletePrefixAsync(BlobNames.WorkOriginal(workId));
        await db.Works.Where(w => w.Id == workId && w.ArtistId == userId).ExecuteDeleteAsync();
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
