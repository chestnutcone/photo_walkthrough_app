using Vela.Api.Common;

namespace Vela.Api.Admin;

public enum ModerationOutcome
{
    Ok, ReasonRequired, ReasonTooLong, NotFound, NotApplicable, Forbidden, CannotBanSelf, CannotBanAdmin,
    TargetStillBlocked, VariantsUnavailable, AlreadyResolved,
    EvidenceCopyFailed, AlreadySubmitted, NotConfigured, SubmissionFailed, RestoreTooEarly, DeliveryFailed, DmcaProcessRequired, EvidenceHeld,
}

public sealed record ReportItem(Guid Id, string Source, string Reason, string Status, string TargetType, Guid TargetId, DateTimeOffset CreatedAt);

public sealed record ModerationCounts(int Pending, int Reviewed, int Actioned, int Dismissed, DateTimeOffset? OldestPendingAt);

public sealed record AdminUserItem(
    Guid UserId, string? Handle, string? DisplayName, bool IsAdmin, bool IsSuspended, bool IsDeleted, DateTimeOffset CreatedAt);

public sealed record AuditItem(Guid Id, Guid AdminId, string Action, string TargetType, Guid TargetId, string Reason, DateTimeOffset At);

public sealed record ChapterText(int Index, string Title, string Body);

/// <summary>Everything the report page shows. Text fields are raw user input: the pages encode them, nothing here does.</summary>
public sealed record ReportDetail(
    ReportItem Item, string? Note, Guid? ReporterId, string? ReporterHandle,
    Guid? OwnerId, string? OwnerHandle, bool OwnerIsAdmin, bool OwnerSuspended,
    string? WorkTitle, string? WorkIntro, string? WorkStatus, bool WorkReportHidden, IReadOnlyList<ChapterText> Chapters,
    string? ReplyText, string? ReplyAuthorHandle, bool ReplyHidden, string? ReplyHiddenBy, Guid? ReplyWorkId,
    int OwnerReportCount, int OwnerRemovals, Guid? EvidenceHoldId = null);

public sealed record UserDetail(AdminUserItem User, int BannedIdentityCount, int ReportsAbout, int Removals, IReadOnlyList<AuditItem> RecentActions, bool EvidenceHeld = false);

public interface IModerationService
{
    /// <summary>Oldest first (created, then id). <paramref name="status"/> null means every status.</summary>
    Task<PagedResult<ReportItem>> QueueAsync(ReportStatus? status, string? reason, string? source, string? cursor, int limit);
    Task<ModerationCounts> CountsAsync();
    Task<ReportDetail?> GetReportAsync(Guid reportId);
    Task<PagedResult<AdminUserItem>> FindUsersAsync(string q, string? cursor, int limit = 25);
    Task<UserDetail?> GetUserAsync(Guid userId);
    /// <summary>Newest first.</summary>
    Task<PagedResult<AuditItem>> AuditAsync(string? cursor, int limit);

    /// <summary>The reason is optional here; the audit row says "dismissed" without one.</summary>
    Task<ModerationOutcome> DismissReportAsync(Guid adminId, Guid reportId, string? reason = null);
    Task<ModerationOutcome> RestoreAsync(Guid adminId, Guid reportId, string reason);
    Task<ModerationOutcome> HideReplyAsync(Guid adminId, Guid replyId, string reason);
    Task<ModerationOutcome> RemoveWorkAsync(Guid adminId, Guid workId, string reason);
    /// <summary>Shown in the UI as "Ban account".</summary>
    Task<ModerationOutcome> SuspendUserAsync(Guid adminId, Guid userId, string reason);
    Task<ModerationOutcome> ReinstateUserAsync(Guid adminId, Guid userId, string reason);
    /// <summary>Restore step of the DMCA counter-notice process; no admin form calls it (the DMCA service gates it).</summary>
    Task<ModerationOutcome> RestoreAfterDmcaAsync(Guid adminId, Guid reportId, string reason);
    /// <summary>
    /// Holds the evidence first (snapshot plus blob copies; a failure takes nothing down), then takes the content down,
    /// bans the owner (never an admin) and resolves the pending reports. Marking the same target again returns the same hold.
    /// </summary>
    Task<MarkIllegalResult> MarkIllegalAsync(Guid adminId, Guid reportId, string reason);
}
