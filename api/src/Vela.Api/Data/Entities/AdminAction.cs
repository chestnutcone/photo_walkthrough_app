namespace Vela.Api.Data.Entities;

/// <summary>
/// Audit trail of an admin action. No foreign key to users on purpose: the trail must outlive the admin's (and the target's)
/// account, and account deletion never touches it.
/// </summary>
public class AdminAction
{
    public Guid Id { get; init; }
    public Guid AdminId { get; init; }
    /// <summary>dismiss | restore | hide_reply | remove_work | ban_user | reinstate_user.</summary>
    public string Action { get; init; } = "";
    /// <summary>report | work | reply | user.</summary>
    public string TargetType { get; init; } = "";
    public Guid TargetId { get; init; }
    public string Reason { get; init; } = "";
    public DateTimeOffset At { get; init; }
}
