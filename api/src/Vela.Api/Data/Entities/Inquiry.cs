namespace Vela.Api.Data.Entities;

public class Inquiry
{
    public Guid Id { get; init; }
    public Guid SenderId { get; init; }
    public Guid ArtistId { get; init; }
    /// <summary>One of the five contract inquiry types.</summary>
    public string Type { get; init; } = "";
    /// <summary>The sender's reply address, as typed.</summary>
    public string SenderEmail { get; init; } = "";
    public Guid? WorkId { get; init; }
    public string Message { get; init; } = "";
    /// <summary>The caller's address when the inquiry was sent; backs the per-IP limit.</summary>
    public string? ClientIp { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    /// <summary>Null until the artist notification was handed to the email provider.</summary>
    public DateTimeOffset? EmailedAt { get; set; }
}
