using Microsoft.EntityFrameworkCore;
using Vela.Api.Common;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Profiles;
using Vela.Api.Social;
using Vela.Api.Works;

namespace Vela.Api.Feed;

public enum FeedTab { Following, Discover }

public interface IFeedService
{
    Task<PagedResult<WorkDto>> GetAsync(Guid? viewerId, FeedTab tab, string? cursor, int limit);
    Task<PagedResult<WorkDto>> ListArtistWorksAsync(Guid? viewerId, string handle, string? cursor, int limit);
}

/// <summary>
/// Feed and artist work lists. Order is (PublishedAt desc, Id desc); the following tab puts works by followed artists in
/// segment 0 and everyone else's in segment 1, and the cursor carries the segment so paging crosses the boundary exactly.
/// </summary>
public sealed class FeedService(VelaDbContext db, IVisibility visibility, WorkReader reader, ArtistQueries artists) : IFeedService
{
    private const int FollowedSegment = 0;
    private const int RestSegment = 1;

    private sealed record Row(Work Work, int Segment);

    public async Task<PagedResult<WorkDto>> GetAsync(Guid? viewerId, FeedTab tab, string? cursor, int limit)
    {
        var position = cursor is null ? (Cursor.Position?)null : Cursor.DecodeSegmented(cursor);
        var visible = await PublicPublishedAsync(viewerId);
        var segmented = tab == FeedTab.Following && viewerId is { } v && await db.Follows.AnyAsync(f => f.FollowerId == v);

        List<Row> rows;
        if (!segmented)
            rows = (await TakeAfter(visible, position, limit + 1)).Select(w => new Row(w, RestSegment)).ToList();
        else
        {
            var me = viewerId!.Value;
            var followed = visible.Where(w => db.Follows.Any(f => f.FollowerId == me && f.FolloweeId == w.ArtistId));
            var rest = visible.Where(w => !db.Follows.Any(f => f.FollowerId == me && f.FolloweeId == w.ArtistId));
            rows = [];
            if (position is null || position.Value.Segment == FollowedSegment)
                rows.AddRange((await TakeAfter(followed, position, limit + 1)).Select(w => new Row(w, FollowedSegment)));
            if (rows.Count <= limit)
            {
                var restPosition = position is { Segment: RestSegment } ? position : null;
                rows.AddRange((await TakeAfter(rest, restPosition, limit + 1 - rows.Count)).Select(w => new Row(w, RestSegment)));
            }
        }
        return await PageAsync(rows, limit, viewerId, segmented);
    }

    public async Task<PagedResult<WorkDto>> ListArtistWorksAsync(Guid? viewerId, string handle, string? cursor, int limit)
    {
        var position = cursor is null ? (Cursor.Position?)null : Cursor.DecodeSegmented(cursor);
        var artist = await artists.FindByHandleAsync(handle, viewerId) ?? throw FollowService.NotFound();
        var visible = (await PublicPublishedAsync(viewerId)).Where(w => w.ArtistId == artist.Profile.UserId);
        var works = await TakeAfter(visible, position, limit + 1);
        return await PageAsync(works.Select(w => new Row(w, RestSegment)).ToList(), limit, viewerId, segmented: false);
    }

    private async Task<IQueryable<Work>> PublicPublishedAsync(Guid? viewerId) =>
        WorkVisibility.Public(db, await visibility.HiddenUserIdsAsync(viewerId)).AsNoTracking().Where(w => w.PublishedAt != null);

    private static Task<List<Work>> TakeAfter(IQueryable<Work> q, Cursor.Position? after, int take)
    {
        if (take <= 0) return Task.FromResult(new List<Work>());
        if (after is { } c)
            q = q.Where(w => w.PublishedAt < c.At || (w.PublishedAt == c.At && w.Id.CompareTo(c.Id) < 0));
        return q.OrderByDescending(w => w.PublishedAt).ThenByDescending(w => w.Id).Take(take).ToListAsync();
    }

    private async Task<PagedResult<WorkDto>> PageAsync(List<Row> rows, int limit, Guid? viewerId, bool segmented)
    {
        var page = rows.Take(limit).ToList();
        var next = rows.Count > limit
            ? Cursor.Encode(segmented ? page[^1].Segment : 0, page[^1].Work.PublishedAt!.Value, page[^1].Work.Id)
            : null;
        return new PagedResult<WorkDto>(await reader.BuildAsync(page.Select(r => r.Work).ToList(), viewerId), next);
    }
}
