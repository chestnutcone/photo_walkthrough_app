using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;

namespace Vela.Api.Social;

/// <summary>Users a viewer must not see: those they blocked plus those who blocked them.</summary>
public interface IVisibility
{
    Task<IReadOnlySet<Guid>> HiddenUserIdsAsync(Guid? viewerId);
}

/// <summary>Scoped (one per request); the set per viewer is loaded once and cached.</summary>
public sealed class Visibility(VelaDbContext db) : IVisibility
{
    private static readonly IReadOnlySet<Guid> Empty = new HashSet<Guid>();
    private readonly Dictionary<Guid, IReadOnlySet<Guid>> _cache = [];

    public async Task<IReadOnlySet<Guid>> HiddenUserIdsAsync(Guid? viewerId)
    {
        if (viewerId is not { } id) return Empty;
        if (_cache.TryGetValue(id, out var cached)) return cached;
        var blocked = await db.Blocks.AsNoTracking().Where(b => b.BlockerId == id).Select(b => b.BlockedId).ToListAsync();
        var blockers = await db.Blocks.AsNoTracking().Where(b => b.BlockedId == id).Select(b => b.BlockerId).ToListAsync();
        return _cache[id] = blocked.Concat(blockers).ToHashSet();
    }
}
