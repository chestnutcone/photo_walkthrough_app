namespace Vela.Api.Data.Entities;

/// <summary>One-time sign-in code. Not user-owned: it exists before an account does.</summary>
public class EmailCode
{
    public Guid Id { get; init; }
    public string Email { get; init; } = "";
    public string CodeHash { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public int Attempts { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}
