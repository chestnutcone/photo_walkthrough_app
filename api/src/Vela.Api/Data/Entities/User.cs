namespace Vela.Api.Data.Entities;

public class User
{
    public Guid Id { get; init; }
    public Guid TierId { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset? SuspendedAt { get; set; }
    /// <summary>Hard deletions that failed so far; the sweep skips the account for a while after each failure.</summary>
    public int DeletionFailures { get; set; }
    public DateTimeOffset? DeletionFailedAt { get; set; }
    public bool IsAdmin { get; set; }
    public DateTimeOffset? AgeConfirmedAt { get; set; }
    public DateTimeOffset? TermsAcceptedAt { get; set; }
}
