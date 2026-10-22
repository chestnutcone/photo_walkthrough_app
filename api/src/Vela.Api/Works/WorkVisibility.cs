using Vela.Api.Data;
using Vela.Api.Data.Entities;

namespace Vela.Api.Works;

/// <summary>
/// The one definition of "this work is public to this viewer": published, not hidden by a report, its owner exists, is
/// not deleted or suspended and is not in the viewer's block set. Single reads, the feed, artist lists and replies all use it.
/// </summary>
public static class WorkVisibility
{
    public static IQueryable<Work> Public(VelaDbContext db, IReadOnlyCollection<Guid> hiddenUsers)
    {
        var hidden = hiddenUsers.ToArray();
        return db.Works.Where(w => w.Status == WorkStatus.Published && !w.ReportHidden
            && !hidden.Contains(w.ArtistId)
            && db.Profiles.Any(p => p.UserId == w.ArtistId)
            && db.Users.Any(u => u.Id == w.ArtistId && u.DeletedAt == null && u.SuspendedAt == null));
    }
}
