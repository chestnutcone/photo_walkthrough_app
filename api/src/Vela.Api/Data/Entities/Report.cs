namespace Vela.Api.Data.Entities;

public class Report
{
    public Guid Id { get; init; }
    /// <summary>user | auto_flag | scan_unavailable.</summary>
    public string Source { get; init; } = "";
    public string Reason { get; init; } = "";
    /// <summary>open | restored | confirmed | dismissed.</summary>
    public string Status { get; set; } = "open";
    /// <summary>work | reply | profile.</summary>
    public string TargetType { get; init; } = "";
    public Guid TargetId { get; init; }
    public Guid? ReporterId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
