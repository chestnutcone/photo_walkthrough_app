namespace Vela.Api.Data.Entities;

/// <summary>
/// Illegal-content evidence kept for law enforcement. No foreign key to users on purpose: account deletion must not purge it.
/// Never exposed by a public API, the owner's export, notifications or search.
/// </summary>
public class EvidenceHold
{
    public Guid Id { get; init; }
    /// <summary>The content owner (plain id, no foreign key).</summary>
    public Guid UserId { get; init; }
    public Guid? WorkId { get; init; }
    public Guid? ReplyId { get; init; }
    /// <summary>work | reply | user.</summary>
    public string TargetType { get; init; } = "";
    /// <summary>"evidence/{id}/": where the copied blobs live in the private evidence container.</summary>
    public string BlobPrefix { get; init; } = "";
    public string Reason { get; init; } = "";
    public DateTimeOffset HoldUntil { get; set; }
    public string? NcmecReportId { get; set; }
    public DateTimeOffset? NcmecSubmittedAt { get; set; }
    /// <summary>JSON (jsonb) copy of the content, its owner and the reports at the time of the hold.</summary>
    public string Snapshot { get; init; } = "{}";
    public Guid CreatedByAdminId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
