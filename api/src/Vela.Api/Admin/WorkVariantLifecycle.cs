using Microsoft.EntityFrameworkCore;
using Vela.Api.Account;
using Vela.Api.Data;
using Vela.Api.Images;

namespace Vela.Api.Admin;

/// <summary>Takes a work's public image variants offline (and back) without touching the private original.</summary>
public interface IWorkVariantLifecycle
{
    /// <summary>Deletes works/{id}/thumb|feed|full.webp and purges them plus the share page <c>/s/{id}</c> from the CDN.</summary>
    Task TakeDownAsync(Guid workId);

    /// <summary>Regenerates the variants from the original and records the work's dimensions. Throws when the original cannot be read.</summary>
    Task RestoreAsync(Guid workId);
}

public sealed class WorkVariantLifecycle(IBlobStore blobs, IVariantGenerator generator, ICdnPurger cdn, VelaDbContext db) : IWorkVariantLifecycle
{
    private static readonly string[] Names = ["thumb", "feed", "full"];

    public async Task TakeDownAsync(Guid workId)
    {
        await blobs.DeletePrefixAsync(BlobNames.WorkVariantsPrefix(workId));
        var urls = Names.Select(n => blobs.PublicUrl($"{BlobNames.WorkVariantsPrefix(workId)}{n}.webp").ToString())
            .Append($"/s/{workId}").ToList();
        await cdn.PurgeAsync(urls);
    }

    public async Task RestoreAsync(Guid workId)
    {
        var made = await generator.GenerateAsync(BlobNames.WorkOriginal(workId));
        await db.Works.Where(w => w.Id == workId)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Width, made.Width).SetProperty(w => w.Height, made.Height));
    }
}
