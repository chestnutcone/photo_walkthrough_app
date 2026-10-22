namespace Vela.Api.Data.Entities;

public class ExportJob
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    /// <summary>pending | running | ready | failed | expired.</summary>
    public string Status { get; set; } = "pending";
    public DateTimeOffset RequestedAt { get; init; }
    /// <summary>When a worker claimed the job; a job stuck in running is reclaimed after a while.</summary>
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    /// <summary>When the archive stops being downloadable (7 days after it is ready).</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? ArchiveBlob { get; set; }
}
