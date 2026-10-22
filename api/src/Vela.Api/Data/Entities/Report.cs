namespace Vela.Api.Data.Entities;

public class Report
{
    public Guid Id { get; init; }
    /// <summary>user_report | auto_flag | dmca | scan_unavailable.</summary>
    public string Source { get; init; } = "";
    public string Reason { get; init; } = "";
    /// <summary>pending | reviewed | actioned | dismissed.</summary>
    public string Status { get; set; } = "pending";
    /// <summary>work | reply | user.</summary>
    public string TargetType { get; init; } = "";
    public Guid TargetId { get; init; }
    /// <summary>Optional reporter note (max 500); scrubbed when the reporter is deleted.</summary>
    public string? Note { get; set; }
    public Guid? ReporterId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
