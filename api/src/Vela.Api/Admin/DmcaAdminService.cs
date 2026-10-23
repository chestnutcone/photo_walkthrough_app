using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vela.Api.Auth;
using Vela.Api.Branding;
using Vela.Api.Common;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Legal;

namespace Vela.Api.Admin;

/// <summary>Admin decisions on DMCA notices. Every decision is audited (target type <c>dmca_notice</c>).</summary>
public sealed partial class DmcaAdminService(
    VelaDbContext db, TimeProvider clock, IModerationService moderation, IEmailSender email, IOptions<BrandingOptions> branding,
    ILogger<DmcaAdminService> log) : IDmcaAdminService
{
    public static readonly TimeSpan WaitingPeriod = TimeSpan.FromDays(10);
    public const string OwnerSubject = "Your story was removed after a copyright notice";
    public const string ClaimantSubject = "Counter-notice received";
    public const string RestoreReason = "DMCA counter-notice";

    public async Task<IReadOnlyList<DmcaNoticeView>> NoticesForWorkAsync(Guid workId)
    {
        var rows = await db.DmcaNotices.AsNoTracking().Where(n => n.WorkId == workId).OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).ToListAsync();
        return [.. rows.Select(n => new DmcaNoticeView(n.Id, n.Status, n.CreatedAt, n.ClaimantName, n.ClaimantEmail, n.ClaimantAddress, n.ClaimantPhone,
            n.WorkReference, n.OriginalWorkDescription, n.SignatureName, n.TakedownAt, n.CounterName, n.CounterAddress, n.CounterPhone,
            n.CounterStatement, n.CounterAt, n.ForwardedAt, n.ForwardedAt + WaitingPeriod))];
    }

    public Task<Guid?> WorkOfAsync(Guid noticeId) =>
        db.DmcaNotices.AsNoTracking().Where(n => n.Id == noticeId).Select(n => n.WorkId).SingleOrDefaultAsync();

    public Task<int> TakedownCountAsync(Guid ownerId) =>
        db.DmcaNotices.AsNoTracking().CountAsync(n => n.OwnerUserId == ownerId && n.TakedownAt != null);

    public async Task<ModerationOutcome> TakeDownAsync(Guid adminId, Guid noticeId)
    {
        if (await GateAsync(adminId) is { } refused) return refused;
        var n = await db.DmcaNotices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == noticeId);
        if (n is null) return ModerationOutcome.NotFound;
        if (n.Status != DmcaStatus.Pending || n.WorkId is not { } workId) return ModerationOutcome.NotApplicable;

        var removed = await moderation.RemoveWorkAsync(adminId, workId, $"DMCA notice #{noticeId}");
        if (removed != ModerationOutcome.Ok) return removed;
        var now = clock.GetUtcNow();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var changed = await db.DmcaNotices.Where(x => x.Id == noticeId && x.Status == DmcaStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, DmcaStatus.Takedown).SetProperty(x => x.TakedownAt, now));
            if (changed == 0) return ModerationOutcome.AlreadyResolved;
            Audit(adminId, "dmca_takedown", noticeId, $"work {workId} removed", now);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        await NotifyOwnerAsync(n);
        return ModerationOutcome.Ok;
    }

    public async Task<ModerationOutcome> RejectAsync(Guid adminId, Guid noticeId, string? reason)
    {
        if (await GateAsync(adminId) is { } refused) return refused;
        var text = reason?.Trim() ?? "";
        if (text.Length > ModerationService.MaxReasonLength) return ModerationOutcome.ReasonTooLong;
        var n = await db.DmcaNotices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == noticeId);
        if (n is null) return ModerationOutcome.NotFound;
        if (n.Status != DmcaStatus.Pending) return ModerationOutcome.NotApplicable;

        var now = clock.GetUtcNow();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var changed = await db.DmcaNotices.Where(x => x.Id == noticeId && x.Status == DmcaStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, DmcaStatus.Rejected));
            if (changed == 0) return ModerationOutcome.AlreadyResolved;
            Audit(adminId, "dmca_reject", noticeId, text.Length == 0 ? "rejected" : text, now);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        await CloseReportWhenNothingPendingAsync(adminId, n.WorkId);
        return ModerationOutcome.Ok;
    }

    /// <summary>When the last pending notice on a work is rejected, the queue item is dismissed too (not while content is still auto-blocked).</summary>
    private async Task CloseReportWhenNothingPendingAsync(Guid adminId, Guid? workId)
    {
        if (workId is not { } id || await db.DmcaNotices.AnyAsync(x => x.WorkId == id && x.Status == DmcaStatus.Pending)) return;
        var report = await db.Reports.AsNoTracking().Where(r => r.Source == DmcaService.ReportSource && r.TargetType == "work" && r.TargetId == id && r.Status == "pending")
            .Select(r => r.Id).FirstOrDefaultAsync();
        if (report != Guid.Empty) await moderation.DismissReportAsync(adminId, report, "DMCA notice rejected");
    }

    private async Task NotifyOwnerAsync(DmcaNotice n)
    {
        try
        {
            if (n.OwnerUserId is not { } owner || await LoginEmail.ForUserAsync(db, owner) is not { } to)
            {
                log.LogInformation("DMCA takedown email skipped ({Reason}).", "no_recipient");
                return;
            }
            var title = await db.Works.AsNoTracking().Where(w => w.Id == n.WorkId).Select(w => w.Title).FirstOrDefaultAsync() ?? "";
            var app = branding.Value.AppName;
            await email.SendAsync(to, OwnerSubject,
                $"Your story \"{title}\" on {app} was removed after we received a copyright notice.\n\n" +
                $"Notice reference: {n.Id}\nClaimant: {n.ClaimantName}\n\n" +
                "If you believe the notice is mistaken or the story was removed by error, you can send a counter-notice. " +
                $"Open the app, go to your removed story and choose to submit a counter-notice. You will be asked for your name, address, " +
                "a statement under penalty of perjury and your consent to the jurisdiction of the courts. " +
                "We will pass it to the claimant, and the story can be restored 10 days later unless the claimant tells us otherwise.\n");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning("DMCA takedown email failed ({Reason}).", e.GetType().Name);
        }
    }

    private async Task<ModerationOutcome?> GateAsync(Guid adminId) =>
        await db.Users.AsNoTracking().AnyAsync(u => u.Id == adminId && u.IsAdmin && u.DeletedAt == null && u.SuspendedAt == null)
            ? null : ModerationOutcome.Forbidden;

    private void Audit(Guid adminId, string action, Guid noticeId, string reason, DateTimeOffset at) =>
        db.AdminActions.Add(new AdminAction
        {
            Id = Guid.NewGuid(), AdminId = adminId, Action = action, TargetType = "dmca_notice", TargetId = noticeId,
            Reason = reason.Length > ModerationService.MaxReasonLength ? reason[..ModerationService.MaxReasonLength] : reason, At = at,
        });
}
