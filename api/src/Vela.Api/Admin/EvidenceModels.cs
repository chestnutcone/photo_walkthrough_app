using Vela.Api.Common;

namespace Vela.Api.Admin;

// ---- the stored snapshot (camelCase JSON in evidence_holds.snapshot) ----

public sealed record SnapshotIdentity(string Provider, string? Email);

public sealed record SnapshotProfile(
    string DisplayName, string Bio, string City, string Discipline, string Availability, string Website, string Social,
    string Studio, string ReplyTime, string? PublicEmail);

public sealed record SnapshotOwner(
    Guid UserId, string? Handle, string? DisplayName, IReadOnlyList<SnapshotIdentity> Identities, SnapshotProfile? Profile);

public sealed record SnapshotChapter(int Index, double X, double Y, double? Zoom, string Title, string Body, string? Meta, string? AltText);

public sealed record SnapshotWork(
    Guid Id, string Title, string Intro, string Status, string? AltText, int Width, int Height,
    DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt, IReadOnlyList<SnapshotChapter> Chapters);

public sealed record SnapshotReply(
    Guid Id, Guid WorkId, int ChapterIndex, Guid AuthorId, string? AuthorHandle, string Text, bool Hidden, DateTimeOffset CreatedAt);

public sealed record SnapshotReport(Guid Id, string Source, string Reason, string Status, DateTimeOffset CreatedAt);

public sealed record EvidenceSnapshot(
    int Version, string TargetType, DateTimeOffset CapturedAt, SnapshotOwner Owner, SnapshotWork? Work, SnapshotReply? Reply,
    IReadOnlyList<SnapshotReport> Reports, bool OriginalCopied, IReadOnlyList<string> Files);

// ---- admin views ----

public sealed record EvidenceListItem(
    Guid Id, DateTimeOffset CreatedAt, string TargetType, string? OwnerHandle, DateTimeOffset HoldUntil, string? NcmecReportId, DateTimeOffset? NcmecSubmittedAt);

public sealed record EvidenceDetail(
    EvidenceListItem Item, Guid UserId, Guid? WorkId, Guid? ReplyId, string Reason, Guid CreatedByAdminId, EvidenceSnapshot? Snapshot);

public sealed record EvidenceCapture(ModerationOutcome Outcome, Data.Entities.EvidenceHold? Hold);

public sealed record MarkIllegalResult(ModerationOutcome Outcome, Guid? HoldId, bool BanRefused);

public interface IEvidenceService
{
    /// <summary>Copies the blobs and commits the hold (and its audit entry); idempotent per target.</summary>
    Task<EvidenceCapture> CaptureAsync(Guid adminId, Data.Entities.Report report, string reason);
    /// <summary>Newest first.</summary>
    Task<PagedResult<EvidenceListItem>> ListAsync(string? cursor, int limit);
    Task<EvidenceDetail?> GetAsync(Guid holdId);
    /// <summary>
    /// Submits the hold to NCMEC. ONLY the admin action on the hold detail page may call this (a test scans the source for
    /// other callers). One submission per hold.
    /// </summary>
    Task<ModerationOutcome> SubmitToNcmecAsync(Guid adminId, Guid holdId, string reason, CancellationToken ct = default);
}
