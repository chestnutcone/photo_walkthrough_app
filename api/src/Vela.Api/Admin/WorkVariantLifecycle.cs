using Microsoft.EntityFrameworkCore;
using Vela.Api.Account;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Images;

namespace Vela.Api.Admin;

/// <summary>Takes a work's public image variants offline (and back) without touching the private original.</summary>
public interface IWorkVariantLifecycle
{
    /// <summary>Deletes works/{id}/thumb|feed|full.webp, purges them plus the share page <c>/s/{id}</c>, then stamps <c>TakedownDoneAt</c> (blocked/removed works).</summary>
    Task TakeDownAsync(Guid workId);

    /// <summary>Regenerates the variants from the original and records the work's dimensions. Throws when the original cannot be read.</summary>
    Task RestoreAsync(Guid workId);
}

public sealed class WorkVariantLifecycle(IBlobStore blobs, IVariantGenerator generator, ICdnPurger cdn, VelaDbContext db, TimeProvider clock) : IWorkVariantLifecycle
{
    private static readonly string[] Names = ["thumb", "feed", "full"];

    public async Task TakeDownAsync(Guid workId)
    {
        await blobs.DeletePrefixAsync(BlobNames.WorkVariantsPrefix(workId));
        var urls = Names.Select(n => blobs.PublicUrl($"{BlobNames.WorkVariantsPrefix(workId)}{n}.webp").ToString())
            .Append($"/s/{workId}").ToList();
        await cdn.PurgeAsync(urls);
        // Only now is the take-down complete; blocked/removed rows without this mark are retried by the reconciler.
        var now = clock.GetUtcNow();
        await db.Works.Where(w => w.Id == workId && (w.Status == WorkStatus.Blocked || w.Status == WorkStatus.Removed))
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.TakedownDoneAt, now));
    }

    public async Task RestoreAsync(Guid workId)
    {
        var made = await generator.GenerateAsync(BlobNames.WorkOriginal(workId), workId);
        await db.Works.Where(w => w.Id == workId)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Width, made.Width).SetProperty(w => w.Height, made.Height));
    }
}
