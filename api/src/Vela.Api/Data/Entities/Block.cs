namespace Vela.Api.Data.Entities;

public class Block
{
    public Guid BlockerId { get; init; }
    public Guid BlockedId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
