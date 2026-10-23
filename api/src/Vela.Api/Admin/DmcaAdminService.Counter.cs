using Microsoft.EntityFrameworkCore;
using Vela.Api.Data.Entities;
using Vela.Api.Legal;

namespace Vela.Api.Admin;

public sealed partial class DmcaAdminService
{
    public async Task<ModerationOutcome> ForwardAsync(Guid adminId, Guid noticeId)
    {
        if (await GateAsync(adminId) is { } refused) return refused;
        var n = await db.DmcaNotices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == noticeId);
        if (n is null) return ModerationOutcome.NotFound;
        if (n.Status != DmcaStatus.CounterNotice) return ModerationOutcome.NotApplicable;

        // Claim the notice first (committed), so only one caller can ever reach the email below; ForwardedAt, which starts the
        // waiting period, is set only once the claimant has really been told.
        var claimed = await db.DmcaNotices.Where(x => x.Id == noticeId && x.Status == DmcaStatus.CounterNotice)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, DmcaStatus.Forwarded));
        if (claimed == 0) return ModerationOutcome.AlreadyResolved;
        try { await email.SendAsync(n.ClaimantEmail, ClaimantSubject, ClaimantBody(n)); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError("Forwarding a counter-notice failed ({Reason}); the claim was rolled back.", e.GetType().Name);
            await db.DmcaNotices.Where(x => x.Id == noticeId && x.Status == DmcaStatus.Forwarded && x.ForwardedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, DmcaStatus.CounterNotice));
            return ModerationOutcome.DeliveryFailed;
        }
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.DmcaNotices.Where(x => x.Id == noticeId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ForwardedAt, now));
        Audit(adminId, "dmca_forward", noticeId, "counter-notice forwarded to the claimant", now);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return ModerationOutcome.Ok;
    }

    private string ClaimantBody(DmcaNotice n) =>
        $"We received a counter-notice about your copyright notice {n.Id} on {branding.Value.AppName}.\n\n" +
        $"Name: {n.CounterName}\nAddress: {n.CounterAddress}\nPhone: {n.CounterPhone ?? "not given"}\n\nStatement:\n{n.CounterStatement}\n\n" +
        "The person consents to the jurisdiction of the courts and states, under penalty of perjury, that the story was removed by mistake or misidentification. " +
        "The story may be restored 10 days after this message unless you tell us that you have filed a court action to keep it down.\n";

    public async Task<ModerationOutcome> RestoreAsync(Guid adminId, Guid noticeId, bool claimantWithdrew, string? reason)
    {
        if (await GateAsync(adminId) is { } refused) return refused;
        var text = reason?.Trim() ?? "";
        if (claimantWithdrew && text.Length == 0) return ModerationOutcome.ReasonRequired;
        if (text.Length > ModerationService.MaxReasonLength) return ModerationOutcome.ReasonTooLong;
        var n = await db.DmcaNotices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == noticeId);
        if (n is null) return ModerationOutcome.NotFound;
        if (n.Status != DmcaStatus.Forwarded || n.ForwardedAt is not { } forwarded || n.WorkId is not { } workId) return ModerationOutcome.NotApplicable;
        var now = clock.GetUtcNow();
        if (!claimantWithdrew && forwarded + WaitingPeriod > now) return ModerationOutcome.RestoreTooEarly;

        var report = await db.Reports.AsNoTracking().Where(r => r.Source == DmcaService.ReportSource && r.TargetType == "work" && r.TargetId == workId)
            .OrderByDescending(r => r.CreatedAt).Select(r => r.Id).FirstOrDefaultAsync();
        if (report == Guid.Empty) return ModerationOutcome.NotFound;
        var restored = await moderation.RestoreAfterDmcaAsync(adminId, report, RestoreReason);
        if (restored != ModerationOutcome.Ok) return restored;

        await using var tx = await db.Database.BeginTransactionAsync();
        await db.DmcaNotices.Where(x => x.Id == noticeId && x.Status == DmcaStatus.Forwarded)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, DmcaStatus.Restored));
        Audit(adminId, "dmca_restore", noticeId, claimantWithdrew ? $"claimant withdrew: {text}" : "waiting period elapsed", now);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return ModerationOutcome.Ok;
    }
}
