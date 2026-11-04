using Vela.Api.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Vela.Api.Data;
using Vela.Api.Data.Entities;

namespace Vela.Api.Social;

public sealed class ReportOptions
{
    public const string Section = "Reports";
    public int MaxPerUserPerDay { get; set; } = 10;
    /// <summary>Distinct reporters (any account age) whose pending reports hide an item (S2 ruling).</summary>
    public int HideAfterDistinctReporters { get; set; } = 3;
    /// <summary>A reporter whose account is older than this hides the item with one report (S2 ruling).</summary>
    public int EstablishedAccountDays { get; set; } = 7;
    /// <summary>
    /// An illegal-content report hides the item at once only when the reporter's account is at least this old
    /// (owner decision 2026-11-03, review I-5); a younger account's report waits in the queue and counts as a pending reporter.
    /// </summary>
    public int IllegalContentInstantHideMinAgeHours { get; set; } = 24;
}

public interface IReportService
{
    Task CreateAsync(Guid reporterId, string targetType, Guid targetId, string reason, string? note);
}

public sealed class ReportService(VelaDbContext db, TimeProvider clock, IOptions<ReportOptions> options, IVisibility visibility) : IReportService
{
    public const string SourceUser = "user_report";
    public const string SourceAutoFlag = "auto_flag";
    public const string Pending = "pending";
    /// <summary>The report reason that hides the item at once when the reporter's account is old enough.</summary>
    public const string IllegalContent = "illegal_content";

    public async Task CreateAsync(Guid reporterId, string targetType, Guid targetId, string reason, string? note)
    {
        var ownerId = await FindVisibleOwnerAsync(reporterId, targetType, targetId)
            ?? throw new SafetyException(StatusCodes.Status404NotFound, "target_not_found", "The reported item no longer exists.");
        if (ownerId == reporterId)
            throw new SafetyException(StatusCodes.Status400BadRequest, "cannot_report_own", "You cannot report your own content.");
        if (await db.Reports.AnyAsync(r => r.ReporterId == reporterId && r.TargetType == targetType && r.TargetId == targetId)) return;

        var now = clock.GetUtcNow();
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var today = await db.Reports.CountAsync(r => r.ReporterId == reporterId && r.Source == SourceUser && r.CreatedAt >= dayStart);
        if (today >= options.Value.MaxPerUserPerDay)
            throw new SafetyException(StatusCodes.Status429TooManyRequests, "report_limit_reached",
                "You have reached today's report limit. Try again tomorrow.", dayStart.AddDays(1) - now);

        db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(), Source = SourceUser, Reason = reason, Note = note, Status = Pending,
            TargetType = targetType, TargetId = targetId, ReporterId = reporterId, CreatedAt = now,
        });
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            // One report decision at a time per target, so parallel reporters cannot all count below the threshold.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended('report:' || {targetId.ToString()}, 0))");
            await db.SaveChangesAsync();
            if (await ShouldHideAsync(reporterId, targetType, targetId, reason, now)) await HideAsync(targetType, targetId);
            await tx.CommitAsync();
            VelaMetrics.ReportCreated(targetType);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent identical report won the race: idempotent.
        }
    }

    /// <summary>
    /// Adds an auto-flag to the caller's unit of work without saving, so it commits in the caller's transaction together with
    /// the block (a fresh draft has no earlier flag, so no duplicate check is needed).
    /// </summary>
    public void AddAutoFlag(string targetType, Guid targetId, string category, int severity) =>
        db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(), Source = SourceAutoFlag, Reason = $"auto:{category}:{severity}", Status = Pending,
            TargetType = targetType, TargetId = targetId, ReporterId = null, CreatedAt = clock.GetUtcNow(),
        });

    /// <summary>Queues an auto-flag for admin review; the caller applies the automatic block. Idempotent while pending.</summary>
    public async Task CreateAutoFlagAsync(string targetType, Guid targetId, string category, int severity)
    {
        if (await db.Reports.AnyAsync(r => r.TargetType == targetType && r.TargetId == targetId
                && r.Source == SourceAutoFlag && r.Status == Pending)) return;
        db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(), Source = SourceAutoFlag, Reason = $"auto:{category}:{severity}", Status = Pending,
            TargetType = targetType, TargetId = targetId, ReporterId = null, CreatedAt = clock.GetUtcNow(),
        });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Owner of the target if the reporter could see it; null (reported as target_not_found) otherwise.
    /// Works must be published, replies not hidden on a published work, and neither may belong to a blocked relation.
    /// </summary>
    private async Task<Guid?> FindVisibleOwnerAsync(Guid reporterId, string targetType, Guid id)
    {
        var hidden = (await visibility.HiddenUserIdsAsync(reporterId)).ToArray();
        return targetType switch
        {
            "work" => await db.Works.Where(w => w.Id == id && w.Status == WorkStatus.Published && !hidden.Contains(w.ArtistId))
                .Select(w => (Guid?)w.ArtistId).FirstOrDefaultAsync(),
            "reply" => await db.Replies.Where(r => r.Id == id && !r.Hidden && !hidden.Contains(r.AuthorId))
                .Where(r => db.Works.Any(w => w.Id == r.WorkId && w.Status == WorkStatus.Published && !hidden.Contains(w.ArtistId)))
                .Select(r => (Guid?)r.AuthorId).FirstOrDefaultAsync(),
            "user" => await db.Users.Where(u => u.Id == id && u.DeletedAt == null && !hidden.Contains(u.Id))
                .Select(u => (Guid?)u.Id).FirstOrDefaultAsync(),
            _ => null,
        };
    }

    /// <summary>
    /// S2 ruling (report brigading, R-2) and owner decision 2026-11-03: an illegal-content report hides at once when the
    /// reporter's account is at least <see cref="ReportOptions.IllegalContentInstantHideMinAgeHours"/> old; any report hides once
    /// <see cref="ReportOptions.HideAfterDistinctReporters"/> distinct accounts have pending reports on the item, or the reporter's
    /// account is older than <see cref="ReportOptions.EstablishedAccountDays"/>. Below that the report only waits in the queue.
    /// </summary>
    private async Task<bool> ShouldHideAsync(Guid reporterId, string targetType, Guid targetId, string reason, DateTimeOffset now)
    {
        var o = options.Value;
        if (reason == IllegalContent)
        {
            var oldEnoughAt = now - TimeSpan.FromHours(o.IllegalContentInstantHideMinAgeHours);
            if (await db.Users.AnyAsync(u => u.Id == reporterId && u.CreatedAt <= oldEnoughAt)) return true;
        }
        var establishedBefore = now - TimeSpan.FromDays(o.EstablishedAccountDays);
        if (await db.Users.AnyAsync(u => u.Id == reporterId && u.CreatedAt < establishedBefore)) return true;
        var reporters = await db.Reports
            .Where(r => r.TargetType == targetType && r.TargetId == targetId && r.Source == SourceUser && r.Status == Pending && r.ReporterId != null)
            // Accounts suspended or deleted since they reported no longer count (review M-14).
            .Where(r => db.Users.Any(u => u.Id == r.ReporterId && u.SuspendedAt == null && u.DeletedAt == null))
            .Select(r => r.ReporterId).Distinct().CountAsync();
        return reporters >= o.HideAfterDistinctReporters;
    }

    private async Task HideAsync(string targetType, Guid id)
    {
        if (targetType == "work")
            await db.Works.Where(w => w.Id == id).ExecuteUpdateAsync(s => s.SetProperty(w => w.ReportHidden, true));
        else if (targetType == "reply")
            await db.Replies.Where(r => r.Id == id && !(r.Hidden && r.HiddenBy == HiddenBy.Admin))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Hidden, true).SetProperty(r => r.HiddenBy, HiddenBy.Report));
    }
}
