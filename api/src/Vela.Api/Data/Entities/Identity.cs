namespace Vela.Api.Data.Entities;

public class Identity
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string Provider { get; init; } = "";
    public string Subject { get; init; } = "";
    public string? Email { get; set; }
}
