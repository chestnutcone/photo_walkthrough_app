using Microsoft.EntityFrameworkCore;
using Vela.Api.Auth;
using Vela.Api.Data;

namespace Vela.Api.Account;

public interface IAccountDeletionService
{
    /// <summary>Marks the account deleted and revokes every session now. The hard delete follows in <see cref="DeletionWorker"/>.</summary>
    Task RequestAsync(Guid userId);
}

public sealed class AccountDeletionService(VelaDbContext db, ITokenService tokens, IUserStateCache stateCache, TimeProvider clock, ILogger<AccountDeletionService> log)
    : IAccountDeletionService
{
    public async Task RequestAsync(Guid userId)
    {
        var now = clock.GetUtcNow();
        // Only the first request stamps the time, so repeating the call (or racing it) changes nothing.
        var marked = await db.Users.Where(u => u.Id == userId && u.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAt, now));
        await tokens.RevokeAllAsync(userId);
        stateCache.Evict(userId);
        log.LogInformation("Account deletion requested for {UserId} (newly marked: {Marked}).", userId, marked > 0);
    }
}
