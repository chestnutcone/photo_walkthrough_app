namespace Vela.Api.Data.Entities;

public class ExportJob
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    /// <summary>pending | running | ready | failed.</summary>
    public string Status { get; set; } = "pending";
    public DateTimeOffset RequestedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ArchiveBlob { get; set; }
}
