using Microsoft.EntityFrameworkCore;
using Npgsql;
using Vela.Api.Data;
using Vela.Api.Data.Entities;

namespace Vela.Api.Auth.Identity;

/// <summary>Maps (provider, subject) to a user. Never matches or links by email.</summary>
public sealed class AccountResolver(VelaDbContext db, TimeProvider clock) : IAccountResolver
{
    public async Task<(Guid UserId, bool IsNew)> SignInAsync(VerifiedIdentity identity)
    {
        if (await FindAsync(identity) is { } existing) return (existing, false);

        var user = new User { Id = Guid.NewGuid(), TierId = Tier.FreemiumId, CreatedAt = clock.GetUtcNow() };
        db.Users.Add(user);
        db.Identities.Add(new Data.Entities.Identity
        {
            Id = Guid.NewGuid(), UserId = user.Id, Provider = identity.Provider,
            Subject = identity.Subject, Email = identity.Email,
        });
        try
        {
            await db.SaveChangesAsync();
            return (user.Id, true);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Lost a concurrent first-sign-in race: use the winner's user.
            db.ChangeTracker.Clear();
            var winner = await FindAsync(identity) ?? throw new InvalidOperationException("Identity vanished after conflict.", e);
            return (winner, false);
        }
    }

    private async Task<Guid?> FindAsync(VerifiedIdentity identity)
    {
        var row = await db.Identities.AsNoTracking()
            .Where(i => i.Provider == identity.Provider && i.Subject == identity.Subject)
            .Join(db.Users, i => i.UserId, u => u.Id, (i, u) => new { u.Id, u.SuspendedAt, u.DeletedAt })
            .SingleOrDefaultAsync();
        if (row is null) return null;
        if (row.DeletedAt is not null) throw new AccountDeletedException();
        if (row.SuspendedAt is not null) throw new AccountSuspendedException();
        return row.Id;
    }
}
