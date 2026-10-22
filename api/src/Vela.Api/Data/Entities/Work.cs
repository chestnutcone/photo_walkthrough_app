namespace Vela.Api.Data.Entities;

public class Work
{
    public Guid Id { get; init; }
    public Guid ArtistId { get; init; }
    public string Title { get; set; } = "";
    public string Intro { get; set; } = "";
    public WorkStatus Status { get; set; }
    /// <summary>Hidden pending review after a user report.</summary>
    public bool ReportHidden { get; set; }
    /// <summary>Set when an admin restores the work; it is then never auto-blocked again.</summary>
    public bool ModerationOverride { get; set; }
    public string ImageBlob { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public string? AltText { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    /// <summary>When publish was requested; the moderation sweep picks up works that stay pending.</summary>
    public DateTimeOffset? PendingSince { get; set; }
    /// <summary>Failed moderation scans so far (fail-open after 3).</summary>
    public int ScanAttempts { get; set; }
    public DateTimeOffset? LastScanAttemptAt { get; set; }
}
