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
}

public interface IReportService
{
    Task CreateAsync(Guid reporterId, string targetType, Guid targetId, string reason, string? note);
}

public sealed class ReportService(VelaDbContext db, TimeProvider clock, IOptions<ReportOptions> options) : IReportService
{
    public const string SourceUser = "user_report";
    public const string SourceAutoFlag = "auto_flag";
    public const string Pending = "pending";

    public async Task CreateAsync(Guid reporterId, string targetType, Guid targetId, string reason, string? note)
    {
        var ownerId = await FindOwnerAsync(targetType, targetId)
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
            await db.SaveChangesAsync();
            await HideAsync(targetType, targetId);
            await tx.CommitAsync();
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent identical report won the race: idempotent.
        }
    }

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

    private async Task<Guid?> FindOwnerAsync(string targetType, Guid id) => targetType switch
    {
        "work" => await db.Works.Where(w => w.Id == id).Select(w => (Guid?)w.ArtistId).FirstOrDefaultAsync(),
        "reply" => await db.Replies.Where(r => r.Id == id).Select(r => (Guid?)r.AuthorId).FirstOrDefaultAsync(),
        "user" => await db.Users.Where(u => u.Id == id && u.DeletedAt == null).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(),
        _ => null,
    };

    private async Task HideAsync(string targetType, Guid id)
    {
        if (targetType == "work")
            await db.Works.Where(w => w.Id == id).ExecuteUpdateAsync(s => s.SetProperty(w => w.ReportHidden, true));
        else if (targetType == "reply")
            await db.Replies.Where(r => r.Id == id && !(r.Hidden && r.HiddenBy == HiddenBy.Admin))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Hidden, true).SetProperty(r => r.HiddenBy, HiddenBy.Report));
    }
}
