using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vela.Api.Branding;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Images;
using Vela.Api.Social;

namespace Vela.Api.Works;

/// <summary>Reads a work for one viewer, applying visibility rules and building the public DTO.</summary>
public sealed class WorkReader(VelaDbContext db, IVisibility visibility, IBlobStore blobs, IOptions<BrandingOptions> branding)
{
    public async Task<WorkDto?> GetAsync(Guid workId, Guid? viewerId)
    {
        var work = await db.Works.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workId);
        if (work is null) return null;
        var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == work.ArtistId);
        var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == work.ArtistId);
        if (owner is null || profile is null) return null;

        var isOwner = viewerId == work.ArtistId;
        var isAdmin = viewerId is { } v && !isOwner && await db.Users.AnyAsync(u => u.Id == v && u.IsAdmin && u.DeletedAt == null);
        var hidden = await visibility.HiddenUserIdsAsync(viewerId);
        var publiclyVisible = work.Status == WorkStatus.Published && !work.ReportHidden
            && owner.DeletedAt is null && owner.SuspendedAt is null && !hidden.Contains(work.ArtistId);
        if (!isOwner && !isAdmin && !publiclyVisible) return null;

        var counts = await ReplyCountsAsync(workId, hidden);
        var chapters = (await db.Chapters.AsNoTracking().Where(c => c.WorkId == workId).OrderBy(c => c.Index).ToListAsync())
            .Select(c => new ChapterDto(c.X, c.Y, c.Zoom, c.Title, c.Body, c.Meta, c.AltText, counts.GetValueOrDefault(c.Index)))
            .ToList();
        return ToDto(work, profile, chapters, isOwner, showStatus: isOwner || isAdmin);
    }

    private async Task<Dictionary<int, int>> ReplyCountsAsync(Guid workId, IReadOnlySet<Guid> hiddenUsers)
    {
        var ids = hiddenUsers.ToArray();
        return await db.Replies.AsNoTracking()
            .Where(r => r.WorkId == workId && !r.Hidden && !ids.Contains(r.AuthorId))
            .GroupBy(r => r.ChapterIndex).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
    }

    private WorkDto ToDto(Work w, Profile p, List<ChapterDto> chapters, bool isOwner, bool showStatus)
    {
        var hasImage = w.Width > 0 && w.Height > 0;
        string? Variant(string name) => hasImage ? blobs.PublicUrl($"{BlobNames.WorkVariantsPrefix(w.Id)}{name}.webp").ToString() : null;
        var avatar = p.AvatarBlob is { Length: > 0 } a ? blobs.PublicUrl(a).ToString() : null;
        return new WorkDto(
            w.Id, new ArtistSummaryDto(p.UserId, p.Handle, p.DisplayName, avatar), w.Title, w.Intro, w.AltText, isOwner,
            showStatus ? w.Status.ToString().ToLowerInvariant() : null,
            Variant("full"), Variant("thumb"), Variant("feed"),
            hasImage ? w.Width : null, hasImage ? w.Height : null,
            chapters, w.CreatedAt, w.PublishedAt,
            isOwner && w.Status == WorkStatus.Draft ? w.ExpiresAt : null,
            $"https://{branding.Value.Domain}/w/{w.Id}");
    }
}
