using Microsoft.EntityFrameworkCore;
using Vela.Api.Common;
using Vela.Api.Data;
using Vela.Api.Profiles;

namespace Vela.Api.Social;

public interface IFollowService
{
    Task FollowAsync(Guid userId, Guid artistId);
    Task UnfollowAsync(Guid userId, Guid artistId);
    Task<PagedResult<ArtistDto>> ListFollowersAsync(Guid? viewerId, Guid artistId, string? cursor, int limit);
}

public sealed class FollowService(
    VelaDbContext db, TimeProvider clock, IDomainEventBus bus, IVisibility visibility,
    ArtistQueries artists, ArtistPresenter presenter) : IFollowService
{
    public static SafetyException NotFound() =>
        new(StatusCodes.Status404NotFound, "artist_not_found", "No such artist.");

    public async Task FollowAsync(Guid userId, Guid artistId)
    {
        if (userId == artistId)
            throw new SafetyException(StatusCodes.Status400BadRequest, "cannot_follow_self", "You cannot follow yourself.");

        await using var tx = await db.Database.BeginTransactionAsync();
        await PairLock.AcquireAsync(db, userId, artistId);
        // Checked after the lock: a block that committed first is seen here, a later block waits and then removes this follow.
        var blocked = await db.Blocks.AnyAsync(b =>
            (b.BlockerId == userId && b.BlockedId == artistId) || (b.BlockerId == artistId && b.BlockedId == userId));
        if (blocked || !await ActiveArtistAsync(artistId)) throw NotFound();

        var created = await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO follows (follower_id, followee_id, created_at) VALUES ({userId}, {artistId}, {clock.GetUtcNow()}) ON CONFLICT DO NOTHING");
        if (created == 1) await bus.PublishAsync(new Followed(userId, artistId), CancellationToken.None);
        await tx.CommitAsync();
    }

    public Task UnfollowAsync(Guid userId, Guid artistId) =>
        db.Follows.Where(f => f.FollowerId == userId && f.FolloweeId == artistId).ExecuteDeleteAsync();

    public async Task<PagedResult<ArtistDto>> ListFollowersAsync(Guid? viewerId, Guid artistId, string? cursor, int limit)
    {
        var position = cursor is null ? (Cursor.Position?)null : Cursor.DecodeSegmented(cursor);
        if (!await artists.IsVisibleAsync(artistId, viewerId)) throw NotFound();
        var hidden = (await visibility.HiddenUserIdsAsync(viewerId)).ToArray();

        var q = db.Follows.AsNoTracking().Where(f => f.FolloweeId == artistId
            && !hidden.Contains(f.FollowerId)
            && db.Profiles.Any(p => p.UserId == f.FollowerId)
            && db.Users.Any(u => u.Id == f.FollowerId && u.DeletedAt == null && u.SuspendedAt == null
                && u.AgeConfirmedAt != null && u.TermsAcceptedAt != null));
        if (position is { } c)
            q = q.Where(f => f.CreatedAt < c.At || (f.CreatedAt == c.At && f.FollowerId.CompareTo(c.Id) < 0));

        var rows = await q.OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.FollowerId).Take(limit + 1)
            .Select(f => new { f.FollowerId, f.CreatedAt }).ToListAsync();
        var page = rows.Take(limit).ToList();
        var next = rows.Count > limit ? Cursor.Encode(page[^1].CreatedAt, page[^1].FollowerId) : null;
        return new PagedResult<ArtistDto>(await presenter.PresentAsync(page.Select(r => r.FollowerId).ToList(), viewerId), next);
    }

    private Task<bool> ActiveArtistAsync(Guid id) =>
        db.Profiles.AnyAsync(p => p.UserId == id && db.Users.Any(u => u.Id == id
            && u.DeletedAt == null && u.SuspendedAt == null && u.AgeConfirmedAt != null && u.TermsAcceptedAt != null));
}
