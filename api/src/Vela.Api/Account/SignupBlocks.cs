using Microsoft.EntityFrameworkCore;
using Vela.Api.Admin;
using Vela.Api.Data;

namespace Vela.Api.Account;

/// <summary>
/// Owner decision 2026-11-03: re-signup block after deleting an account during review. Rows hold only the identity hash and
/// dates. A block applies while <c>expires_at</c> is in the future (checked at sign-in), is removed with the account's hard delete
/// (the review closed or the 30-day cap passed) and, as a backstop, by the hourly <see cref="Auth.AuthDataRetentionJob"/>.
/// </summary>
public sealed class SignupBlocks(VelaDbContext db, AdminSettings settings)
{
    /// <summary>Blocks every sign-in identity of the user until <paramref name="until"/>. Idempotent (the request and the sweep both call it).</summary>
    public async Task BlockAsync(Guid userId, DateTimeOffset now, DateTimeOffset until)
    {
        foreach (var hash in await HashesOfAsync(userId))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO signup_blocks (subject_hash, created_at, expires_at) VALUES ({hash}, {now}, {until}) ON CONFLICT (subject_hash) DO UPDATE SET expires_at = GREATEST(signup_blocks.expires_at, EXCLUDED.expires_at)");
    }

    public async Task<bool> IsBlockedAsync(string provider, string subject, DateTimeOffset now)
    {
        var hash = BanHasher.Hash(settings.BanPepper, provider, subject);
        return await db.SignupBlocks.AsNoTracking().AnyAsync(b => b.SubjectHash == hash && b.ExpiresAt > now);
    }

    /// <summary>Removes the user's blocks; called by the hard delete before the identity rows go.</summary>
    public async Task<int> LiftAsync(Guid userId, CancellationToken ct = default)
    {
        var hashes = await HashesOfAsync(userId);
        return await db.SignupBlocks.Where(b => hashes.Contains(b.SubjectHash)).ExecuteDeleteAsync(ct);
    }

    public Task<int> DeleteLapsedAsync(DateTimeOffset now, CancellationToken ct = default) =>
        db.SignupBlocks.Where(b => b.ExpiresAt <= now).ExecuteDeleteAsync(ct);

    private async Task<List<string>> HashesOfAsync(Guid userId) =>
        (await db.Identities.AsNoTracking().Where(i => i.UserId == userId).Select(i => new { i.Provider, i.Subject }).ToListAsync())
            .Select(i => BanHasher.Hash(settings.BanPepper, i.Provider, i.Subject)).Distinct().ToList();
}
