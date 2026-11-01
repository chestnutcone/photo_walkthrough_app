using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;
using Vela.Api.Data.Entities;

namespace Vela.Api.Works;

/// <summary>
/// Shared count of the works that occupy a quota slot: draft, pending and published
/// (not blocked or removed). Used by GET /me and by the work-creation quota check.
/// </summary>
public sealed class WorkCounts(VelaDbContext db)
{
    public Task<int> CountActiveAsync(Guid userId) =>
        db.Works.CountAsync(w => w.ArtistId == userId
            && (w.Status == WorkStatus.Draft || w.Status == WorkStatus.Pending || w.Status == WorkStatus.Published));
}
