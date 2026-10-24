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
    /// <summary>Set by the sweep that claimed this account for hard deletion; others skip it until it passes.</summary>
    public DateTimeOffset? DeletionLeaseUntil { get; set; }
    public bool IsAdmin { get; set; }
    /// <summary>Identifies the one live admin cookie session. Set at admin sign-in, cleared at sign-out: a copied cookie stops working then.</summary>
    public Guid? AdminSessionStamp { get; set; }
    public DateTimeOffset? AgeConfirmedAt { get; set; }
    public DateTimeOffset? TermsAcceptedAt { get; set; }
}
