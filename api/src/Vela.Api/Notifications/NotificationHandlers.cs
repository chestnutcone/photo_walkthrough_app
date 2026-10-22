using Microsoft.EntityFrameworkCore;
using Vela.Api.Common;
using Vela.Api.Data;
using Vela.Api.Data.Entities;

namespace Vela.Api.Notifications;

/// <summary>Shared checks for handlers: a recipient can be notified only if active and not in a block relation with the actor.</summary>
internal static class NotificationRules
{
    public static async Task<bool> CanNotifyAsync(VelaDbContext db, Guid recipientId, Guid actorId, CancellationToken ct)
    {
        if (recipientId == actorId) return false;
        if (!await db.Users.AnyAsync(u => u.Id == recipientId && u.DeletedAt == null && u.SuspendedAt == null, ct)) return false;
        return !await db.Blocks.AnyAsync(b =>
            (b.BlockerId == recipientId && b.BlockedId == actorId) || (b.BlockerId == actorId && b.BlockedId == recipientId), ct);
    }
}

/// <summary>A reply notifies the story's owner. Runs inside the replying transaction and skips (never throws) on missing data.</summary>
public sealed class ReplyNotificationHandler(VelaDbContext db, TimeProvider clock) : IDomainEventHandler<ReplyAdded>
{
    public async Task HandleAsync(ReplyAdded evt, CancellationToken ct)
    {
        var ownerId = await db.Works.AsNoTracking().Where(w => w.Id == evt.WorkId).Select(w => (Guid?)w.ArtistId).FirstOrDefaultAsync(ct);
        var chapter = await db.Replies.AsNoTracking().Where(r => r.Id == evt.ReplyId).Select(r => (int?)r.ChapterIndex).FirstOrDefaultAsync(ct);
        if (ownerId is not { } owner || chapter is not { } index) return;
        if (!await NotificationRules.CanNotifyAsync(db, owner, evt.AuthorId, ct)) return;

        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(), UserId = owner, Type = NotificationTypes.Reply, ActorId = evt.AuthorId,
            WorkId = evt.WorkId, ChapterIndex = index, ReplyId = evt.ReplyId, CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>A new follower notifies the artist; an unread notification from the same follower is not repeated.</summary>
public sealed class FollowerNotificationHandler(VelaDbContext db, TimeProvider clock) : IDomainEventHandler<Followed>
{
    public async Task HandleAsync(Followed evt, CancellationToken ct)
    {
        if (!await NotificationRules.CanNotifyAsync(db, evt.ArtistId, evt.FollowerId, ct)) return;
        if (await db.Notifications.AnyAsync(n => n.UserId == evt.ArtistId && n.Type == NotificationTypes.Follower
            && n.ActorId == evt.FollowerId && n.ReadAt == null, ct)) return;

        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(), UserId = evt.ArtistId, Type = NotificationTypes.Follower, ActorId = evt.FollowerId, CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// A published story notifies every eligible follower with one set-based statement. The partial unique index makes a
/// repeated event for the same story (admin restore) add nothing.
/// </summary>
public sealed class NewStoryNotificationHandler(VelaDbContext db, TimeProvider clock) : IDomainEventHandler<WorkPublished>
{
    public async Task HandleAsync(WorkPublished evt, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notifications (id, user_id, type, actor_id, work_id, created_at)
            SELECT gen_random_uuid(), f.follower_id, 'NewStory', {evt.ArtistId}, {evt.WorkId}, {now}
            FROM follows f
            JOIN users fu ON fu.id = f.follower_id AND fu.deleted_at IS NULL AND fu.suspended_at IS NULL
            WHERE f.followee_id = {evt.ArtistId}
              AND EXISTS (SELECT 1 FROM users a WHERE a.id = {evt.ArtistId} AND a.deleted_at IS NULL AND a.suspended_at IS NULL)
              AND NOT EXISTS (SELECT 1 FROM blocks b
                  WHERE (b.blocker_id = f.follower_id AND b.blocked_id = {evt.ArtistId})
                     OR (b.blocker_id = {evt.ArtistId} AND b.blocked_id = f.follower_id))
            ON CONFLICT (user_id, type, work_id) WHERE type = 'NewStory' DO NOTHING
            """, ct);
    }
}
