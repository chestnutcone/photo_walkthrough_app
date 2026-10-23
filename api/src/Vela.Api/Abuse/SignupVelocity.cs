using System.Net;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Common;
using Vela.Api.Data;
using Vela.Api.Data.Entities;

namespace Vela.Api.Abuse;

/// <summary>
/// At most <see cref="AbuseOptions.SignupsPerIpPerDay"/> new accounts per client address per rolling day. The slot is
/// claimed in the same transaction that creates the account, under a per-address advisory lock, so only a committed
/// account counts and concurrent signups cannot slip past the limit together.
/// </summary>
public sealed class SignupVelocity(VelaDbContext db, TimeProvider clock, AbuseOptions options)
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(1);

    /// <summary>Read-only check: would one more account from this address be allowed right now?</summary>
    public async Task<bool> AllowAsync(IPAddress ip)
    {
        var (key, since) = (IpKey.Of(ip), clock.GetUtcNow() - Window);
        return await db.SignupEvents.CountAsync(e => e.IpKey == key && e.CreatedAt > since) < options.SignupsPerIpPerDay;
    }

    /// <summary>
    /// Claims a slot inside the caller's open transaction: the event is added to the context and is persisted (or rolled
    /// back) together with the account. Returns the wait when refused.
    /// </summary>
    public async Task<TimeSpan?> TryClaimAsync(IPAddress ip)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Claiming a signup slot needs an open transaction.");
        var (key, now) = (IpKey.Of(ip), clock.GetUtcNow());
        var since = now - Window;
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({"signup:" + key}))");
        await db.SignupEvents.Where(e => e.CreatedAt < since).ExecuteDeleteAsync();

        var hits = await db.SignupEvents.AsNoTracking().Where(e => e.IpKey == key && e.CreatedAt > since)
            .Select(e => e.CreatedAt).ToListAsync();
        if (RateLimiting.RetryAfter(hits, options.SignupsPerIpPerDay, Window, now) is { } wait) return wait;

        db.SignupEvents.Add(new SignupEvent { Id = Guid.NewGuid(), IpKey = key, CreatedAt = now });
        return null;
    }
}
