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
}
