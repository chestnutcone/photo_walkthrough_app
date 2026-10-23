using Microsoft.EntityFrameworkCore;
using Vela.Api.Common;
using Vela.Api.Data.Entities;

namespace Vela.Api.Admin;

public sealed partial class ModerationService
{
    private const int MaxPage = 100;
    private const int MaxSearchLength = 100;

    public async Task<PagedResult<ReportItem>> QueueAsync(ReportStatus? status, string? reason, string? source, string? cursor, int limit)
    {
        limit = Math.Clamp(limit, 1, MaxPage);
        var q = db.Reports.AsNoTracking().AsQueryable();
        if (status is { } s) { var value = s.ToDb(); q = q.Where(r => r.Status == value); }
        if (!string.IsNullOrWhiteSpace(reason))
        {
            var exact = reason.Trim();
            var prefix = exact + ":"; // "auto" finds every auto:<category>:<severity>
            q = q.Where(r => r.Reason == exact || r.Reason.StartsWith(prefix));
        }
        if (!string.IsNullOrWhiteSpace(source)) { var src = source.Trim(); q = q.Where(r => r.Source == src); }
        if (cursor is not null)
        {
            var (at, id) = Cursor.Decode(cursor);
            q = q.Where(r => r.CreatedAt > at || (r.CreatedAt == at && r.Id.CompareTo(id) > 0));
        }
        var rows = await q.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Take(limit + 1).ToListAsync();
        var page = rows.Take(limit).Select(ToItem).ToList();
        return new PagedResult<ReportItem>(page, rows.Count > limit ? Cursor.Encode(page[^1].CreatedAt, page[^1].Id) : null);
    }

    private static ReportItem ToItem(Report r) => new(r.Id, r.Source, r.Reason, r.Status, r.TargetType, r.TargetId, r.CreatedAt);

    public async Task<ModerationCounts> CountsAsync()
    {
        var byStatus = await db.Reports.AsNoTracking().GroupBy(r => r.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        int Count(ReportStatus s) => byStatus.FirstOrDefault(x => x.Status == s.ToDb())?.Count ?? 0;
        var pending = ReportStatus.Pending.ToDb();
        var oldest = await db.Reports.AsNoTracking().Where(r => r.Status == pending).MinAsync(r => (DateTimeOffset?)r.CreatedAt);
        return new ModerationCounts(Count(ReportStatus.Pending), Count(ReportStatus.Reviewed), Count(ReportStatus.Actioned), Count(ReportStatus.Dismissed), oldest);
    }

    public async Task<PagedResult<AdminUserItem>> FindUsersAsync(string q, string? cursor, int limit = 25)
    {
        limit = Math.Clamp(limit, 1, MaxPage);
        var text = (q ?? "").Trim();
        if (text.Length == 0 || text.Length > MaxSearchLength) return new PagedResult<AdminUserItem>([], null);

        var users = from u in db.Users.AsNoTracking()
                    join p in db.Profiles.AsNoTracking() on u.Id equals p.UserId into pj
                    from p in pj.DefaultIfEmpty()
                    select new { u, p };
        if (Guid.TryParse(text, out var exactId)) users = users.Where(x => x.u.Id == exactId);
        else
        {
            var like = "%" + EscapeLike(text) + "%";
            users = users.Where(x => x.p != null && (EF.Functions.ILike(x.p.Handle, like, "\\") || EF.Functions.ILike(x.p.DisplayName, like, "\\")));
        }
        if (cursor is not null)
        {
            var (at, id) = Cursor.Decode(cursor);
            users = users.Where(x => x.u.CreatedAt > at || (x.u.CreatedAt == at && x.u.Id.CompareTo(id) > 0));
        }
        var rows = await users.OrderBy(x => x.u.CreatedAt).ThenBy(x => x.u.Id).Take(limit + 1)
            .Select(x => new AdminUserItem(x.u.Id, x.p == null ? null : x.p.Handle, x.p == null ? null : x.p.DisplayName,
                x.u.IsAdmin, x.u.SuspendedAt != null, x.u.DeletedAt != null, x.u.CreatedAt)).ToListAsync();
        var page = rows.Take(limit).ToList();
        return new PagedResult<AdminUserItem>(page, rows.Count > limit ? Cursor.Encode(page[^1].CreatedAt, page[^1].UserId) : null);
    }

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public async Task<PagedResult<AuditItem>> AuditAsync(string? cursor, int limit)
    {
        limit = Math.Clamp(limit, 1, MaxPage);
        var q = db.AdminActions.AsNoTracking().AsQueryable();
        if (cursor is not null)
        {
            var (at, id) = Cursor.Decode(cursor);
            q = q.Where(a => a.At < at || (a.At == at && a.Id.CompareTo(id) < 0));
        }
        var rows = await q.OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Take(limit + 1).ToListAsync();
        var page = rows.Take(limit).Select(a => new AuditItem(a.Id, a.AdminId, a.Action, a.TargetType, a.TargetId, a.Reason, a.At)).ToList();
        return new PagedResult<AuditItem>(page, rows.Count > limit ? Cursor.Encode(page[^1].At, page[^1].Id) : null);
    }

    public async Task<UserDetail?> GetUserAsync(Guid userId)
    {
        var item = (await (from u in db.Users.AsNoTracking().Where(u => u.Id == userId)
                           join p in db.Profiles.AsNoTracking() on u.Id equals p.UserId into pj
                           from p in pj.DefaultIfEmpty()
                           select new AdminUserItem(u.Id, p == null ? null : p.Handle, p == null ? null : p.DisplayName,
                               u.IsAdmin, u.SuspendedAt != null, u.DeletedAt != null, u.CreatedAt)).ToListAsync()).SingleOrDefault();
        var bans = await db.BannedIdentities.AsNoTracking().CountAsync(b => b.UserRef == userId);
        // A deleted account has no user row; its ban rows still make it worth showing so the ban can be lifted.
        if (item is null && bans == 0) return null;
        item ??= new AdminUserItem(userId, null, null, false, true, true, default);
        var about = await CountReportsAboutAsync(userId);
        var removals = await CountRemovalsAsync(userId);
        var recent = await db.AdminActions.AsNoTracking().Where(a => a.TargetType == "user" && a.TargetId == userId)
            .OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Take(10)
            .Select(a => new AuditItem(a.Id, a.AdminId, a.Action, a.TargetType, a.TargetId, a.Reason, a.At)).ToListAsync();
        return new UserDetail(item, bans, about, removals, recent);
    }

    public async Task<ReportDetail?> GetReportAsync(Guid reportId)
    {
        var report = await db.Reports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reportId);
        if (report is null) return null;
        var reporter = report.ReporterId is { } rid ? await HandleOfAsync(rid) : null;

        Work? work = null; Reply? reply = null; string? authorHandle = null; Guid? owner = null;
        List<ChapterText> chapters = [];
        switch (report.TargetType)
        {
            case "work":
                work = await db.Works.AsNoTracking().SingleOrDefaultAsync(w => w.Id == report.TargetId);
                owner = work?.ArtistId;
                chapters = await db.Chapters.AsNoTracking().Where(c => c.WorkId == report.TargetId).OrderBy(c => c.Index)
                    .Select(c => new ChapterText(c.Index, c.Title, c.Body)).ToListAsync();
                break;
            case "reply":
                reply = await db.Replies.AsNoTracking().SingleOrDefaultAsync(r => r.Id == report.TargetId);
                owner = reply?.AuthorId;
                authorHandle = owner is { } a ? await HandleOfAsync(a) : null;
                break;
            default:
                owner = report.TargetId;
                break;
        }
        var ownerRow = owner is { } o
            ? await db.Users.AsNoTracking().Where(u => u.Id == o).Select(u => new { u.IsAdmin, Suspended = u.SuspendedAt != null }).SingleOrDefaultAsync()
            : null;
        return new ReportDetail(
            ToItem(report), report.Note, report.ReporterId, reporter,
            owner, owner is { } oh ? await HandleOfAsync(oh) : null, ownerRow?.IsAdmin ?? false, ownerRow?.Suspended ?? false,
            work?.Title, work?.Intro, work?.Status.ToString().ToLowerInvariant(), work?.ReportHidden ?? false, chapters,
            reply?.Text, authorHandle, reply?.Hidden ?? false, reply?.HiddenBy?.ToString().ToLowerInvariant(), reply?.WorkId,
            owner is { } oc ? await CountReportsAboutAsync(oc) : 0, owner is { } or ? await CountRemovalsAsync(or) : 0,
            await EvidenceHoldIdAsync(report));
    }

    private Task<string?> HandleOfAsync(Guid userId) =>
        db.Profiles.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.Handle).SingleOrDefaultAsync();

    private Task<int> CountReportsAboutAsync(Guid userId) =>
        db.Reports.AsNoTracking().CountAsync(r =>
            (r.TargetType == "user" && r.TargetId == userId)
            || (r.TargetType == "work" && db.Works.Any(w => w.Id == r.TargetId && w.ArtistId == userId))
            || (r.TargetType == "reply" && db.Replies.Any(x => x.Id == r.TargetId && x.AuthorId == userId)));

    /// <summary>Works an admin removed plus replies an admin hid.</summary>
    private async Task<int> CountRemovalsAsync(Guid userId) =>
        await db.Works.AsNoTracking().CountAsync(w => w.ArtistId == userId && w.Status == WorkStatus.Removed)
        + await db.Replies.AsNoTracking().CountAsync(r => r.AuthorId == userId && r.HiddenBy == HiddenBy.Admin);

    private async Task<Guid?> EvidenceHoldIdAsync(Report r)
    {
        var id = r.TargetId;
        var holds = db.EvidenceHolds.AsNoTracking();
        return r.TargetType switch
        {
            "work" => await holds.Where(h => h.WorkId == id).Select(h => (Guid?)h.Id).FirstOrDefaultAsync(),
            "reply" => await holds.Where(h => h.ReplyId == id).Select(h => (Guid?)h.Id).FirstOrDefaultAsync(),
            _ => await holds.Where(h => h.TargetType == "user" && h.UserId == id).Select(h => (Guid?)h.Id).FirstOrDefaultAsync(),
        };
    }
}
