namespace Vela.Api.Data.Entities;

public class Inquiry
{
    public Guid Id { get; init; }
    public Guid SenderId { get; init; }
    public Guid ArtistId { get; init; }
    public string Message { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? EmailedAt { get; set; }
}
