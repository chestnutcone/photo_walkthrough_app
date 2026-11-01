using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vela.Api.Branding;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Images;
using Vela.Api.Social;

namespace Vela.Api.Works;

/// <summary>Reads works for one viewer, applying the shared visibility rules and building the public DTOs.</summary>
public sealed class WorkReader(VelaDbContext db, IVisibility visibility, IBlobStore blobs, IOptions<BrandingOptions> branding)
{
    public async Task<WorkDto?> GetAsync(Guid workId, Guid? viewerId) =>
        await FindViewableAsync(workId, viewerId) is { } work ? (await BuildAsync([work], viewerId)).FirstOrDefault() : null;

    /// <summary>The work if this viewer may see it (public, or theirs, or any as admin); null otherwise.</summary>
    public async Task<Work?> FindViewableAsync(Guid workId, Guid? viewerId)
    {
        var work = await db.Works.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workId);
        if (work is null) return null;
        if (viewerId == work.ArtistId || await IsAdminAsync(viewerId)) return work;
        var hidden = await visibility.HiddenUserIdsAsync(viewerId);
        return await WorkVisibility.Public(db, hidden).AnyAsync(w => w.Id == workId) ? work : null;
    }

    /// <summary>The work if it is public to this viewer; owners and admins get no special access (used for writes).</summary>
    public async Task<Work?> FindPublicAsync(Guid workId, Guid? viewerId)
    {
        var hidden = await visibility.HiddenUserIdsAsync(viewerId);
        return await WorkVisibility.Public(db, hidden).AsNoTracking().FirstOrDefaultAsync(w => w.Id == workId);
    }

    /// <summary>DTOs for works already known to be viewable, in the given order; works without a profile are skipped.</summary>
    public async Task<List<WorkDto>> BuildAsync(IReadOnlyList<Work> works, Guid? viewerId)
    {
        if (works.Count == 0) return [];
        var workIds = works.Select(w => w.Id).ToArray();
        var artistIds = works.Select(w => w.ArtistId).Distinct().ToArray();
        var hidden = await visibility.HiddenUserIdsAsync(viewerId);
        var isAdmin = await IsAdminAsync(viewerId);

        var profiles = await db.Profiles.AsNoTracking().Where(p => artistIds.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId);
        var following = viewerId is { } v
            ? (await db.Follows.AsNoTracking().Where(f => f.FollowerId == v && artistIds.Contains(f.FolloweeId)).Select(f => f.FolloweeId).ToListAsync()).ToHashSet()
            : [];
        var chapters = (await db.Chapters.AsNoTracking().Where(c => workIds.Contains(c.WorkId)).OrderBy(c => c.Index).ToListAsync())
            .ToLookup(c => c.WorkId);
        var counts = await ReplyCountsAsync(workIds, hidden);

        return works.Where(w => profiles.ContainsKey(w.ArtistId)).Select(w =>
        {
            var isOwner = viewerId == w.ArtistId;
            var dtos = chapters[w.Id].Select(c =>
                new ChapterDto(c.X, c.Y, c.Zoom, c.Title, c.Body, c.Meta, c.AltText, counts.GetValueOrDefault((w.Id, c.Index)))).ToList();
            return ToDto(w, profiles[w.ArtistId], dtos, isOwner, showStatus: isOwner || isAdmin,
                isFollowing: viewerId is null || isOwner ? null : following.Contains(w.ArtistId));
        }).ToList();
    }

    private Task<bool> IsAdminAsync(Guid? viewerId) =>
        viewerId is { } v ? db.Users.AnyAsync(u => u.Id == v && u.IsAdmin && u.DeletedAt == null) : Task.FromResult(false);

    private async Task<Dictionary<(Guid WorkId, int Index), int>> ReplyCountsAsync(Guid[] workIds, IReadOnlySet<Guid> hiddenUsers) =>
        await ReplyRules.FromVisibleAuthors(db, hiddenUsers).AsNoTracking()
            .Where(r => workIds.Contains(r.WorkId) && !r.Hidden)
            .GroupBy(r => new { r.WorkId, r.ChapterIndex }).Select(g => new { g.Key.WorkId, g.Key.ChapterIndex, Count = g.Count() })
            .ToDictionaryAsync(x => (x.WorkId, x.ChapterIndex), x => x.Count);

    private WorkDto ToDto(Work w, Profile p, List<ChapterDto> chapters, bool isOwner, bool showStatus, bool? isFollowing)
    {
        // Blocked and removed works have their public variants taken down, so no image links are given out for them.
        var hasImage = w.Width > 0 && w.Height > 0 && w.Status is not (WorkStatus.Blocked or WorkStatus.Removed);
        string? Variant(string name) => hasImage ? blobs.PublicUrl($"{BlobNames.WorkVariantsPrefix(w.Id)}{name}.webp").ToString() : null;
        var avatar = p.AvatarBlob is { Length: > 0 } a ? blobs.PublicUrl(a).ToString() : null;
        return new WorkDto(
            w.Id, new ArtistSummaryDto(p.UserId, p.Handle, p.DisplayName, avatar, isFollowing), w.Title, w.Intro, w.AltText, isOwner,
            showStatus ? w.Status.ToString().ToLowerInvariant() : null,
            Variant("full"), Variant("thumb"), Variant("feed"),
            hasImage ? w.Width : null, hasImage ? w.Height : null,
            chapters, w.CreatedAt, w.PublishedAt,
            isOwner && w.Status == WorkStatus.Draft ? w.ExpiresAt : null,
            $"https://{branding.Value.Domain}/s/{w.Id}");
    }
}
