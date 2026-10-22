using Microsoft.EntityFrameworkCore;
using Vela.Api.Common;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Moderation;
using Vela.Api.Profiles;
using Vela.Api.Works;

namespace Vela.Api.Social;

public sealed record ReplyDto(Guid Id, Guid WorkId, int ChapterIndex, ArtistDto Author, string Body, DateTimeOffset CreatedAt, bool HiddenForOthers);

public interface IReplyService
{
    Task<PagedResult<ReplyDto>> ListAsync(Guid? viewerId, Guid workId, int chapterIndex, string? cursor, int limit);
    Task<ReplyDto> AddAsync(Guid userId, Guid workId, int chapterIndex, string text);
    Task DeleteAsync(Guid userId, Guid replyId);
}

public sealed class ReplyService(
    VelaDbContext db, TimeProvider clock, WorkReader works, IVisibility visibility, IContentSafety safety,
    ReportService reports, IDomainEventBus bus, ArtistPresenter presenter, ILogger<ReplyService> log) : IReplyService
{
    public const int BlockSeverity = 4;

    public static SafetyException WorkNotFound() => new(StatusCodes.Status404NotFound, "work_not_found", "No such story.");
    public static SafetyException ChapterNotFound() => new(StatusCodes.Status404NotFound, "chapter_not_found", "No such chapter.");
    public static SafetyException ReplyNotFound() => new(StatusCodes.Status404NotFound, "reply_not_found", "No such reply.");

    public async Task<PagedResult<ReplyDto>> ListAsync(Guid? viewerId, Guid workId, int chapterIndex, string? cursor, int limit)
    {
        var position = cursor is null ? (Cursor.Position?)null : Cursor.DecodeSegmented(cursor);
        if (await works.FindViewableAsync(workId, viewerId) is null) throw WorkNotFound();
        await EnsureChapterAsync(workId, chapterIndex);
        var hidden = await visibility.HiddenUserIdsAsync(viewerId);

        var q = ReplyRules.FromVisibleAuthors(db, hidden).AsNoTracking()
            .Where(r => r.WorkId == workId && r.ChapterIndex == chapterIndex && (!r.Hidden || r.AuthorId == viewerId));
        if (position is { } c)
            q = q.Where(r => r.CreatedAt > c.At || (r.CreatedAt == c.At && r.Id.CompareTo(c.Id) > 0));
        var rows = await q.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Take(limit + 1).ToListAsync();

        var page = rows.Take(limit).ToList();
        var next = rows.Count > limit ? Cursor.Encode(page[^1].CreatedAt, page[^1].Id) : null;
        return new PagedResult<ReplyDto>(await ToDtosAsync(page, viewerId), next);
    }

    public async Task<ReplyDto> AddAsync(Guid userId, Guid workId, int chapterIndex, string text)
    {
        if (!await db.Profiles.AnyAsync(p => p.UserId == userId))
            throw new SafetyException(StatusCodes.Status403Forbidden, "onboarding_required", "Finish onboarding first.");
        if (await works.FindPublicAsync(workId, userId) is null) throw WorkNotFound();
        await EnsureChapterAsync(workId, chapterIndex);
        var body = ReplyText.Validate(text);

        var (severity, scanned) = await ScanAsync(body);
        var flagged = scanned && severity >= BlockSeverity;
        var reply = new Reply
        {
            Id = Guid.NewGuid(), WorkId = workId, ChapterIndex = chapterIndex, AuthorId = userId, Text = body,
            Hidden = flagged, HiddenBy = flagged ? HiddenBy.Auto : null, CreatedAt = clock.GetUtcNow(),
        };

        await using var tx = await db.Database.BeginTransactionAsync();
        db.Replies.Add(reply);
        await db.SaveChangesAsync();
        if (flagged) await reports.CreateAutoFlagAsync("reply", reply.Id, "unsafe_text", severity);
        else if (!scanned) await reports.CreateAutoFlagAsync("reply", reply.Id, "scan_unavailable", 0);
        if (!flagged) await bus.PublishAsync(new ReplyAdded(reply.Id, workId, userId), CancellationToken.None);
        await tx.CommitAsync();
        return (await ToDtosAsync([reply], userId)).Single();
    }

    public async Task DeleteAsync(Guid userId, Guid replyId)
    {
        var target = await db.Replies.AsNoTracking().Where(r => r.Id == replyId)
            .Select(r => new { r.AuthorId, OwnerId = db.Works.Where(w => w.Id == r.WorkId).Select(w => w.ArtistId).First() })
            .FirstOrDefaultAsync();
        if (target is null || (target.AuthorId != userId && target.OwnerId != userId)) throw ReplyNotFound();
        await db.Replies.Where(r => r.Id == replyId).ExecuteDeleteAsync();
    }

    /// <summary>Fail-open: when the scanner is unavailable the reply is kept visible and queued for review.</summary>
    private async Task<(int Severity, bool Scanned)> ScanAsync(string body)
    {
        try { return (await safety.SeverityAsync(body), true); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning("Reply scan unavailable ({Reason}).", e.GetType().Name);
            return (0, false);
        }
    }

    private async Task EnsureChapterAsync(Guid workId, int index)
    {
        if (index < 0 || !await db.Chapters.AnyAsync(c => c.WorkId == workId && c.Index == index)) throw ChapterNotFound();
    }

    private async Task<List<ReplyDto>> ToDtosAsync(IReadOnlyList<Reply> replies, Guid? viewerId)
    {
        var authors = (await presenter.PresentAsync(replies.Select(r => r.AuthorId).Distinct().ToList(), viewerId)).ToDictionary(a => a.Id);
        return replies.Where(r => authors.ContainsKey(r.AuthorId))
            .Select(r => new ReplyDto(r.Id, r.WorkId, r.ChapterIndex, authors[r.AuthorId], r.Text, r.CreatedAt, r.Hidden)).ToList();
    }
}
