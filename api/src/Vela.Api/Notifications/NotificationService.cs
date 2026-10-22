using Microsoft.EntityFrameworkCore;
using Vela.Api.Common;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Profiles;
using Vela.Api.Social;
using Vela.Api.Works;

namespace Vela.Api.Notifications;

/// <summary>One list entry; a group of consecutive unread replies on one chapter is a single item with Count and every row id in Ids.</summary>
public sealed record NotificationDto(
    Guid Id, string Type, ArtistDto Actor, Guid? WorkId, string? WorkTitle, int? ChapterIndex, Guid? ReplyId,
    DateTimeOffset CreatedAt, bool Read, int Count, IReadOnlyList<Guid> Ids);

public interface INotificationService
{
    Task<PagedResult<NotificationDto>> ListAsync(Guid userId, string? cursor, int limit);
    Task<int> UnreadCountAsync(Guid userId);
    Task MarkReadAsync(Guid userId, IReadOnlyList<Guid>? ids);
}

public sealed class NotificationService(
    VelaDbContext db, TimeProvider clock, IVisibility visibility, ArtistPresenter presenter) : INotificationService
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    public async Task<PagedResult<NotificationDto>> ListAsync(Guid userId, string? cursor, int limit)
    {
        var position = cursor is null ? (Cursor.Position?)null : Cursor.DecodeSegmented(cursor);
        limit = Math.Clamp(limit, 1, MaxLimit);
        var q = await VisibleAsync(userId);
        if (position is { } c)
            q = q.Where(n => n.CreatedAt < c.At || (n.CreatedAt == c.At && n.Id.CompareTo(c.Id) < 0));

        var rows = await q.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(limit + 1).ToListAsync();
        var page = rows.Take(limit).ToList();
        var next = rows.Count > limit ? Cursor.Encode(page[^1].CreatedAt, page[^1].Id) : null;
        return new PagedResult<NotificationDto>(await ToDtosAsync(Group(page), userId), next);
    }

    public async Task<int> UnreadCountAsync(Guid userId) => await (await VisibleAsync(userId)).CountAsync(n => n.ReadAt == null);

    public async Task MarkReadAsync(Guid userId, IReadOnlyList<Guid>? ids)
    {
        var now = clock.GetUtcNow();
        var unread = db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null);
        if (ids is { Count: > 0 })
        {
            var wanted = ids.Distinct().ToArray();
            unread = unread.Where(n => wanted.Contains(n.Id));
        }
        await unread.ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now));
    }

    /// <summary>The recipient's rows whose targets still exist and are visible to them (work public, actor active and not hidden).</summary>
    private async Task<IQueryable<Notification>> VisibleAsync(Guid userId)
    {
        var hiddenUsers = await visibility.HiddenUserIdsAsync(userId);
        var hidden = hiddenUsers.ToArray();
        var works = WorkVisibility.Public(db, hiddenUsers).Select(w => w.Id);
        return db.Notifications.AsNoTracking().Where(n => n.UserId == userId
            && n.ActorId != null && !hidden.Contains(n.ActorId.Value)
            && db.Profiles.Any(p => p.UserId == n.ActorId)
            && db.Users.Any(u => u.Id == n.ActorId && u.DeletedAt == null && u.SuspendedAt == null
                && u.AgeConfirmedAt != null && u.TermsAcceptedAt != null)
            && (n.Type == NotificationTypes.Follower || (n.WorkId != null && works.Contains(n.WorkId.Value)))
            && (n.ReplyId == null || db.Replies.Any(r => r.Id == n.ReplyId && !r.Hidden)));
    }

    /// <summary>Consecutive unread replies on the same work chapter become one group (newest first, so the first row is the latest).</summary>
    private static List<List<Notification>> Group(IReadOnlyList<Notification> page)
    {
        var groups = new List<List<Notification>>();
        foreach (var row in page)
        {
            if (groups.Count > 0 && Groups(groups[^1][0], row)) groups[^1].Add(row);
            else groups.Add([row]);
        }
        return groups;
    }

    private static bool Groups(Notification head, Notification row) =>
        head.Type == NotificationTypes.Reply && row.Type == NotificationTypes.Reply && head.ReadAt == null && row.ReadAt == null
        && head.WorkId == row.WorkId && head.ChapterIndex == row.ChapterIndex;

    private async Task<List<NotificationDto>> ToDtosAsync(List<List<Notification>> groups, Guid userId)
    {
        var actors = (await presenter.PresentAsync(groups.Select(g => g[0].ActorId!.Value).Distinct().ToList(), userId)).ToDictionary(a => a.Id);
        var workIds = groups.Select(g => g[0].WorkId).OfType<Guid>().Distinct().ToArray();
        var titles = await db.Works.AsNoTracking().Where(w => workIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Title);
        return groups.Where(g => actors.ContainsKey(g[0].ActorId!.Value)).Select(g =>
        {
            var head = g[0];
            return new NotificationDto(head.Id, NotificationTypes.ToContract(head.Type), actors[head.ActorId!.Value], head.WorkId,
                head.WorkId is { } w && titles.TryGetValue(w, out var t) ? t : null, head.ChapterIndex, head.ReplyId,
                head.CreatedAt, head.ReadAt != null, g.Count, g.Select(n => n.Id).ToList());
        }).ToList();
    }
}
