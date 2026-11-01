using Vela.Api.Data.Entities;

namespace Vela.Api.Admin;

/// <summary>What the report page shows for one notice. Text is raw claimant/owner input: the pages encode it.</summary>
public sealed record DmcaNoticeView(
    Guid Id, string Status, DateTimeOffset CreatedAt, string ClaimantName, string ClaimantEmail, string ClaimantAddress, string? ClaimantPhone,
    string WorkReference, string OriginalWorkDescription, string SignatureName, DateTimeOffset? TakedownAt,
    string? CounterName, string? CounterAddress, string? CounterPhone, string? CounterStatement, DateTimeOffset? CounterAt,
    DateTimeOffset? ForwardedAt, DateTimeOffset? EarliestRestore);

public interface IDmcaAdminService
{
    /// <summary>Newest first; every notice that names this work.</summary>
    Task<IReadOnlyList<DmcaNoticeView>> NoticesForWorkAsync(Guid workId);
    Task<Guid?> WorkOfAsync(Guid noticeId);
    /// <summary>Removes the work, marks the notice <c>takedown</c> and emails the owner.</summary>
    Task<ModerationOutcome> TakeDownAsync(Guid adminId, Guid noticeId);
    Task<ModerationOutcome> RejectAsync(Guid adminId, Guid noticeId, string? reason);
    /// <summary>Emails the counter-notice to the claimant, starts the waiting period and marks the notice <c>forwarded</c>.</summary>
    Task<ModerationOutcome> ForwardAsync(Guid adminId, Guid noticeId);
    /// <summary>Allowed 10 days after forwarding, or at once when the claimant withdrew (with a reason).</summary>
    Task<ModerationOutcome> RestoreAsync(Guid adminId, Guid noticeId, bool claimantWithdrew, string? reason);
    Task<int> TakedownCountAsync(Guid ownerId);
}
