using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;

namespace Vela.Api.Social;

/// <summary>
/// Transaction-scoped advisory lock on an unordered pair of users. Follow creation and blocking both take it, so a follow
/// and a block between the same two people serialise and can never leave a follow row next to a block row.
/// </summary>
public static class PairLock
{
    public static Task AcquireAsync(VelaDbContext db, Guid a, Guid b) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended(least({a}::uuid, {b}::uuid)::text || ':' || greatest({a}::uuid, {b}::uuid)::text, 0))");
}
