namespace Vela.Api.Data.Entities;

/// <summary>
/// A copyright (DMCA) notice sent by a third party. No foreign key to users or works: it is kept for legal defense after the
/// work or the account is deleted. The claimant's details belong to a third party and are never part of the owner's export.
/// </summary>
public class DmcaNotice
{
    public Guid Id { get; init; }
    /// <summary>The matched work; null when the reference did not match one (handled manually).</summary>
    public Guid? WorkId { get; init; }
    public Guid? OwnerUserId { get; init; }
    public string ClaimantName { get; init; } = "";
    public string ClaimantEmail { get; init; } = "";
    /// <summary><see cref="Legal.DmcaService.Mailbox"/> of <see cref="ClaimantEmail"/>, indexed, for the acknowledgement cap (S1b-5). Null on older rows.</summary>
    public string? ClaimantMailbox { get; init; }
    public string ClaimantAddress { get; init; } = "";
    public string? ClaimantPhone { get; init; }
    /// <summary>The submitted URL or text, verbatim.</summary>
    public string WorkReference { get; init; } = "";
    public string OriginalWorkDescription { get; init; } = "";
    public string SignatureName { get; init; } = "";
    public bool GoodFaithStatement { get; init; }
    public bool PerjuryStatement { get; init; }
    /// <summary>pending | rejected | takedown | counter_notice | forwarded | restored.</summary>
    public string Status { get; set; } = DmcaStatus.Pending;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? TakedownAt { get; set; }
    public string? CounterName { get; set; }
    public string? CounterAddress { get; set; }
    public string? CounterPhone { get; set; }
    public string? CounterStatement { get; set; }
    public DateTimeOffset? CounterAt { get; set; }
    public DateTimeOffset? ForwardedAt { get; set; }
    /// <summary>For abuse handling and the per-IP limit.</summary>
    public string? ClientIp { get; init; }
}

public static class DmcaStatus
{
    public const string Pending = "pending";
    public const string Rejected = "rejected";
    public const string Takedown = "takedown";
    public const string CounterNotice = "counter_notice";
    public const string Forwarded = "forwarded";
    public const string Restored = "restored";
}
