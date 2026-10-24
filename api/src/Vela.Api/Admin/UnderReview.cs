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
    /// Pending (not yet scanned) or blocked works, works hidden by a report, works under an evidence hold, and removed works with a
    /// DMCA notice. Drafts, published works and other removed works can be deleted as before.
    /// </summary>
    public async Task<bool> WorkAsync(Work work)
    {
        if (work.Status is WorkStatus.Pending or WorkStatus.Blocked || work.ReportHidden) return true;
        if (await db.EvidenceHolds.AnyAsync(h => h.WorkId == work.Id)) return true;
        return work.Status == WorkStatus.Removed && await db.DmcaNotices.AnyAsync(n => n.WorkId == work.Id);
    }

    /// <summary>Replies hidden automatically or by a report (awaiting an admin), and replies under an evidence hold.</summary>
    public async Task<bool> ReplyAsync(Reply reply) =>
        (reply.Hidden && reply.HiddenBy is HiddenBy.Auto or HiddenBy.Report)
        || await db.EvidenceHolds.AnyAsync(h => h.ReplyId == reply.Id);

    /// <summary>
    /// Whether an account's hard delete must wait: any pending or blocked work, any open report against the user or their works or
    /// replies, any DMCA notice about their works, or any evidence hold on them.
    /// </summary>
    public async Task<bool> AccountAsync(Guid userId)
    {
        var works = db.Works.Where(w => w.ArtistId == userId);
        if (await works.AnyAsync(w => w.Status == WorkStatus.Pending || w.Status == WorkStatus.Blocked)) return true;
        var workIds = works.Select(w => w.Id);
        var replyIds = db.Replies.Where(r => r.AuthorId == userId).Select(r => r.Id);
        if (await db.Reports.AnyAsync(r => r.Status == ReportService.Pending
                && ((r.TargetType == "user" && r.TargetId == userId) || (r.TargetType == "work" && workIds.Contains(r.TargetId))
                    || (r.TargetType == "reply" && replyIds.Contains(r.TargetId))))) return true;
        if (await db.DmcaNotices.AnyAsync(n => n.OwnerUserId == userId || (n.WorkId != null && workIds.Contains(n.WorkId.Value)))) return true;
        return await db.EvidenceHolds.AnyAsync(h => h.UserId == userId);
    }

    public const string Code = "under_review";
}
