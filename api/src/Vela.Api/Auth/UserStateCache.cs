using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;

namespace Vela.Api.Auth;

public enum UserState { Active, Suspended, Deleted, Unknown }

/// <summary>Thrown-as-data failure for a bearer token whose user is suspended, deleted or gone; mapped to 403/401 by the challenge handler.</summary>
public sealed class UserStateAuthException(UserState state) : Exception("The account cannot use this token.")
{
    public UserState State { get; } = state;
}

public interface IUserStateCache
{
    Task<UserState> GetAsync(Guid userId, VelaDbContext db);
    /// <summary>Drops the entry so this instance sees a state change at once (other instances catch up within the TTL).</summary>
    void Evict(Guid userId);
}

/// <summary>Short-lived per-user state so a suspended or deleted user's access token stops working quickly; a hit does no query.</summary>
public sealed class UserStateCache(TimeProvider clock, IConfiguration config) : IUserStateCache
{
    private const int MaxEntries = 20_000;
    private readonly ConcurrentDictionary<Guid, (UserState State, DateTimeOffset Expires)> _entries = new();
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(int.TryParse(config["Auth:UserStateCacheSeconds"], out var s) && s >= 0 ? s : 20);

    public async Task<UserState> GetAsync(Guid userId, VelaDbContext db)
    {
        var now = clock.GetUtcNow();
        if (_entries.TryGetValue(userId, out var hit) && hit.Expires > now) return hit.State;

        var row = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { Suspended = u.SuspendedAt != null, Deleted = u.DeletedAt != null }).SingleOrDefaultAsync();
        var state = row is null ? UserState.Unknown : row.Deleted ? UserState.Deleted : row.Suspended ? UserState.Suspended : UserState.Active;
        if (_entries.Count >= MaxEntries) _entries.Clear();
        _entries[userId] = (state, now + _ttl);
        return state;
    }

    public void Evict(Guid userId) => _entries.TryRemove(userId, out _);
}
