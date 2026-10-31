using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Social;

namespace Vela.Api.Admin;

/// <summary>
/// S1b-2: content that moderation has not finished with. Its owner must not be able to destroy it (and the record of it) in the
/// gap between detection and an admin's decision. Used by work and reply deletion and by account deletion.
/// </summary>
public sealed class UnderReview(VelaDbContext db)
{
    /// <summary>How long account deletion waits for open reviews before it proceeds anyway (the GDPR erasure window).</summary>
    public static readonly TimeSpan AccountDeferralCap = TimeSpan.FromDays(30);

    /// <summary>
    /// Pending (not yet scanned) or blocked works, works hidden by a report, works with any open report (including the fail-open
    /// <c>scan_unavailable</c> flag: never scanned), works under an evidence hold, removed works with a DMCA notice, and works with
    /// any reply under review: deleting the work would cascade those replies away (review round 1, F3/F4).
    /// </summary>
    public async Task<bool> WorkAsync(Work work)
    {
        if (work.Status is WorkStatus.Pending or WorkStatus.Blocked || work.ReportHidden) return true;
        if (await OpenReports("work").AnyAsync(r => r.TargetId == work.Id)) return true;
        if (await db.EvidenceHolds.AnyAsync(h => h.WorkId == work.Id)) return true;
        if (await RepliesUnderReview(db.Replies.Where(r => r.WorkId == work.Id)).AnyAsync()) return true;
        return work.Status == WorkStatus.Removed && await db.DmcaNotices.AnyAsync(n => n.WorkId == work.Id);
    }

    /// <summary>Replies hidden automatically or by a report, replies with any open report (including <c>scan_unavailable</c>), and held replies.</summary>
    public Task<bool> ReplyAsync(Reply reply) => RepliesUnderReview(db.Replies.Where(r => r.Id == reply.Id)).AnyAsync();

    private IQueryable<Report> OpenReports(string targetType) =>
        db.Reports.Where(r => r.Status == ReportService.Pending && r.TargetType == targetType);

    private IQueryable<Reply> RepliesUnderReview(IQueryable<Reply> replies)
    {
        var reported = OpenReports("reply").Select(r => r.TargetId);
        var held = db.EvidenceHolds.Where(h => h.ReplyId != null).Select(h => h.ReplyId!.Value);
        return replies.Where(r => (r.Hidden && (r.HiddenBy == HiddenBy.Auto || r.HiddenBy == HiddenBy.Report))
            || reported.Contains(r.Id) || held.Contains(r.Id));
    }

    /// <summary>
    /// Whether an account's hard delete must wait: any pending or blocked work, any open report against the user or their works
    /// or replies, any reply under review that the user wrote (anywhere) or that sits on the user's works (the delete would
    /// cascade it away), any DMCA notice about them, or any evidence hold on them.
    /// </summary>
    public async Task<bool> AccountAsync(Guid userId)
    {
        var works = db.Works.Where(w => w.ArtistId == userId);
        if (await works.AnyAsync(w => w.Status == WorkStatus.Pending || w.Status == WorkStatus.Blocked || w.ReportHidden)) return true;
        var workIds = works.Select(w => w.Id);
        if (await db.Reports.AnyAsync(r => r.Status == ReportService.Pending
                && ((r.TargetType == "user" && r.TargetId == userId) || (r.TargetType == "work" && workIds.Contains(r.TargetId))))) return true;
        if (await RepliesUnderReview(db.Replies.Where(r => r.AuthorId == userId || workIds.Contains(r.WorkId))).AnyAsync()) return true;
        if (await db.DmcaNotices.AnyAsync(n => n.OwnerUserId == userId || (n.WorkId != null && workIds.Contains(n.WorkId.Value)))) return true;
        return await db.EvidenceHolds.AnyAsync(h => h.UserId == userId);
    }

    public const string Code = "under_review";
}
