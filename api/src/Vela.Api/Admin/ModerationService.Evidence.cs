using Microsoft.EntityFrameworkCore;
using Vela.Api.Data.Entities;

namespace Vela.Api.Admin;

public sealed partial class ModerationService
{
    public async Task<MarkIllegalResult> MarkIllegalAsync(Guid adminId, Guid reportId, string reason)
    {
        if (await GateAsync(adminId, reason) is { } refused) return new MarkIllegalResult(refused, null, OwnerBan.None);
        var report = await db.Reports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reportId);
        if (report is null) return new MarkIllegalResult(ModerationOutcome.NotFound, null, OwnerBan.None);

        // 1) Hold the evidence (blobs copied, row committed). A failure here has changed nothing else.
        var captured = await evidenceService.CaptureAsync(adminId, report, reason.Trim());
        if (captured.Hold is not { } hold) return new MarkIllegalResult(captured.Outcome, null, OwnerBan.None);

        // 2) Take the content down, 3) ban the owner, 4) resolve the reports. Each step is skipped when already done,
        // so repeating the call finishes whatever an earlier failure left open.
        await TakeDownHeldContentAsync(adminId, hold, reason.Trim());
        var ban = await BanHoldOwnerAsync(adminId, hold, reason.Trim());
        await ResolveAsync(hold.TargetType, hold.WorkId ?? hold.ReplyId ?? hold.UserId, ReportStatus.Actioned);
        return new MarkIllegalResult(ModerationOutcome.Ok, hold.Id, ban);
    }

    private async Task TakeDownHeldContentAsync(Guid adminId, EvidenceHold hold, string reason)
    {
        if (hold.WorkId is { } workId)
        {
            var status = await db.Works.AsNoTracking().Where(w => w.Id == workId).Select(w => (WorkStatus?)w.Status).SingleOrDefaultAsync();
            if (status is not null and not WorkStatus.Removed and not WorkStatus.Draft) await RemoveWorkAsync(adminId, workId, reason);
        }
        else if (hold.ReplyId is { } replyId)
        {
            var done = await db.Replies.AsNoTracking().AnyAsync(r => r.Id == replyId && r.Hidden && r.HiddenBy == HiddenBy.Admin);
            if (!done) await HideReplyAsync(adminId, replyId, reason);
        }
    }

    /// <summary>
    /// Bans the owner, including an account whose deletion is pending (its identities still exist until the hard delete), and
    /// says what really happened.
    /// </summary>
    private async Task<OwnerBan> BanHoldOwnerAsync(Guid adminId, EvidenceHold hold, string reason)
    {
        var owner = await db.Users.AsNoTracking().Where(u => u.Id == hold.UserId)
            .Select(u => new { Suspended = u.SuspendedAt != null }).SingleOrDefaultAsync();
        if (owner is null)
            return await db.BannedIdentities.AsNoTracking().AnyAsync(b => b.UserRef == hold.UserId) ? OwnerBan.AlreadyBanned : OwnerBan.OwnerGone;
        if (owner.Suspended) return OwnerBan.AlreadyBanned;
        return await SuspendUserAsync(adminId, hold.UserId, reason) switch
        {
            ModerationOutcome.Ok => OwnerBan.Banned,
            ModerationOutcome.CannotBanAdmin or ModerationOutcome.CannotBanSelf => OwnerBan.Refused,
            ModerationOutcome.NotFound => OwnerBan.OwnerGone, // hard-deleted between the two reads
            var other => throw new InvalidOperationException($"Unexpected ban outcome {other}."),
        };
    }
}
