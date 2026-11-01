using Microsoft.EntityFrameworkCore;
using Vela.Api.Data.Entities;

namespace Vela.Api.Admin;

public sealed partial class EvidenceService
{
    /// <summary>
    /// The one place the NCMEC reporter is called. The hold row is locked for the whole call, so two submissions cannot both
    /// reach NCMEC; a failure leaves the hold untouched so the admin can retry.
    /// </summary>
    public async Task<ModerationOutcome> SubmitToNcmecAsync(Guid adminId, Guid holdId, string reason, CancellationToken ct = default)
    {
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == adminId && u.IsAdmin && u.DeletedAt == null && u.SuspendedAt == null, ct))
            return ModerationOutcome.Forbidden;
        var text = reason?.Trim() ?? "";
        if (text.Length == 0) return ModerationOutcome.ReasonRequired;
        if (text.Length > ModerationService.MaxReasonLength) return ModerationOutcome.ReasonTooLong;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var locked = (await db.EvidenceHolds.FromSqlInterpolated($"SELECT * FROM evidence_holds WHERE id = {holdId} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();
        if (locked is null) return ModerationOutcome.NotFound;
        if (locked.NcmecReportId is not null) return ModerationOutcome.AlreadySubmitted;

        string reportId;
        try { reportId = await reporter.SubmitAsync(locked, ct); }
        catch (NcmecNotConfiguredException) { return ModerationOutcome.NotConfigured; }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError("NCMEC submission failed ({Reason}).", e.GetType().Name);
            return ModerationOutcome.SubmissionFailed;
        }

        var now = clock.GetUtcNow();
        locked.NcmecReportId = reportId;
        locked.NcmecSubmittedAt = now;
        locked.HoldUntil = Max(locked.HoldUntil, now + Retention);
        db.AdminActions.Add(new AdminAction
        {
            Id = Guid.NewGuid(), AdminId = adminId, Action = "ncmec_submit", TargetType = "evidence", TargetId = holdId, Reason = text, At = now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ModerationOutcome.Ok;
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a >= b ? a : b;
}
