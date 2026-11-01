using Microsoft.EntityFrameworkCore;
using Vela.Api.Auth;
using Vela.Api.Data;
using Vela.Api.Admin;

namespace Vela.Api.Account;

/// <summary>What the contract calls AccountDeletion: <c>scheduled</c> (hard delete within minutes) or <c>deferred_review</c>.</summary>
public sealed record AccountDeletionStatus(string State, DateTimeOffset? CompletesBy);

public static class AccountDeletionStates
{
    public const string Scheduled = "scheduled";
    /// <summary>Content is under review: the hard delete waits until the reviews end, at most until <c>CompletesBy</c>.</summary>
    public const string DeferredReview = "deferred_review";
}

public interface IAccountDeletionService
{
    /// <summary>
    /// Marks the account deleted and revokes every session now (the account and its content disappear at once). The hard delete
    /// follows in <see cref="DeletionWorker"/>, deferred while content is under review (S1b-2).
    /// </summary>
    Task<AccountDeletionStatus> RequestAsync(Guid userId);
}

public sealed class AccountDeletionService(
    VelaDbContext db, ITokenService tokens, IUserStateCache stateCache, UnderReview review, TimeProvider clock, ILogger<AccountDeletionService> log)
    : IAccountDeletionService
{
    public async Task<AccountDeletionStatus> RequestAsync(Guid userId)
    {
        var now = clock.GetUtcNow();
        // Only the first request stamps the time, so repeating the call (or racing it) changes nothing.
        var marked = await db.Users.Where(u => u.Id == userId && u.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAt, now));
        await tokens.RevokeAllAsync(userId);
        stateCache.Evict(userId);
        log.LogInformation("Account deletion requested for {UserId} (newly marked: {Marked}).", userId, marked > 0);
        return await DeletionDeferral.EvaluateAsync(db, review, userId, now, log);
    }
}

/// <summary>
/// The S1b-2 deferral rule, shared by the request and the sweep: while content is under review and the 30-day cap has not
/// passed, the hard delete waits and <c>users.deletion_deferred_until</c> records the cap.
/// </summary>
public static class DeletionDeferral
{
    public static async Task<AccountDeletionStatus> EvaluateAsync(VelaDbContext db, UnderReview review, Guid userId, DateTimeOffset now, ILogger log)
    {
        var deletedAt = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DeletedAt).SingleOrDefaultAsync();
        if (deletedAt is not { } requested) return new AccountDeletionStatus(AccountDeletionStates.Scheduled, null);
        var cap = requested + UnderReview.AccountDeferralCap;
        if (now >= cap || !await review.AccountAsync(userId)) return new AccountDeletionStatus(AccountDeletionStates.Scheduled, null);
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletionDeferredUntil, cap));
        log.LogInformation("Hard delete of {UserId} deferred: content under review (until {Cap} at the latest).", userId, cap);
        return new AccountDeletionStatus(AccountDeletionStates.DeferredReview, cap);
    }
}
