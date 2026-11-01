namespace Vela.Api.Data.Entities;

/// <summary>One row per verify call (per-IP limit) or per failed review-code guess (per-email lock).</summary>
public class VerifyAttempt
{
    public const string Verify = "verify";
    public const string ReviewFailure = "review_failure";

    public Guid Id { get; init; }
    public string Email { get; init; } = "";
    public string Ip { get; init; } = "";
    public string Kind { get; init; } = Verify;
    public DateTimeOffset CreatedAt { get; init; }
}
