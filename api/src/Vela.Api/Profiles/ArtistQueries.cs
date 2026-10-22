using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Social;

namespace Vela.Api.Profiles;

/// <summary>Public-profile reads: counts are computed in the database, one query per list.</summary>
public sealed class ArtistQueries(VelaDbContext db, IVisibility visibility)
{
    public const int SuggestedLimit = 12;

    public sealed record Row(Profile Profile, int Followers, int Following, int Works, int Chapters, bool Followed);

    public async Task<Row?> FindByHandleAsync(string handle, Guid? viewerId) =>
        await Rows(await VisibleAsync(HandleRules.Normalize(handle), viewerId), viewerId).FirstOrDefaultAsync();

    /// <summary>Whether the profile exists and is publicly visible to this viewer (active, onboarded, not hidden by a block).</summary>
    public async Task<bool> IsVisibleAsync(Guid userId, Guid? viewerId) =>
        await (await VisibleAsync(handle: null, viewerId)).AnyAsync(p => p.UserId == userId);

    /// <summary>Rows for the given ids in the given order; ids that are not visible to the viewer are skipped.</summary>
    public async Task<List<Row>> ByIdsAsync(IReadOnlyList<Guid> ids, Guid? viewerId)
    {
        var ordered = ids.ToArray();
        var rows = await Rows((await VisibleAsync(handle: null, viewerId)).Where(p => ordered.Contains(p.UserId)), viewerId).ToListAsync();
        return ordered.Select(id => rows.FirstOrDefault(r => r.Profile.UserId == id)).OfType<Row>().ToList();
    }

    /// <summary>The signed-in user's own profile row, even before onboarding completes.</summary>
    public Task<Row?> FindOwnAsync(Guid userId) =>
        Rows(db.Profiles.AsNoTracking().Where(p => p.UserId == userId), userId).FirstOrDefaultAsync();

    public async Task<List<Row>> SuggestedAsync(Guid? viewerId)
    {
        var q = (await VisibleAsync(handle: null, viewerId))
            .Where(x => viewerId == null || (x.UserId != viewerId && !db.Follows.Any(f => f.FollowerId == viewerId && f.FolloweeId == x.UserId)))
            .OrderByDescending(x => db.Follows.Count(f => f.FolloweeId == x.UserId))
            .ThenByDescending(x => db.Users.Where(u => u.Id == x.UserId).Select(u => u.CreatedAt).First())
            .ThenBy(x => x.UserId)
            .Take(SuggestedLimit);
        return await Rows(q, viewerId).ToListAsync();
    }

    private async Task<IQueryable<Profile>> VisibleAsync(string? handle, Guid? viewerId)
    {
        var q = db.Profiles.AsNoTracking().Where(p => db.Users.Any(u => u.Id == p.UserId
            && u.DeletedAt == null && u.SuspendedAt == null
            && u.AgeConfirmedAt != null && u.TermsAcceptedAt != null));
        if (handle is not null) q = q.Where(p => p.Handle == handle);
        return ExcludeBlocked(q, await visibility.HiddenUserIdsAsync(viewerId));
    }

    /// <summary>Hides profiles the viewer blocked and profiles that blocked the viewer.</summary>
    private static IQueryable<Profile> ExcludeBlocked(IQueryable<Profile> source, IReadOnlySet<Guid> hidden)
    {
        if (hidden.Count == 0) return source;
        var ids = hidden.ToArray();
        return source.Where(p => !ids.Contains(p.UserId));
    }

    private IQueryable<Row> Rows(IQueryable<Profile> q, Guid? viewerId) =>
        q.Select(p => new Row(p,
            db.Follows.Count(f => f.FolloweeId == p.UserId),
            db.Follows.Count(f => f.FollowerId == p.UserId),
            db.Works.Count(w => w.ArtistId == p.UserId && w.Status == WorkStatus.Published),
            db.Chapters.Count(c => db.Works.Any(w => w.Id == c.WorkId && w.ArtistId == p.UserId && w.Status == WorkStatus.Published)),
            viewerId != null && db.Follows.Any(f => f.FollowerId == viewerId && f.FolloweeId == p.UserId)));

    public static ArtistDto ToDto(Row r, Guid? viewerId, bool onboardingComplete = true)
    {
        var p = r.Profile;
        return new ArtistDto(p.UserId, p.Handle, p.DisplayName, p.Bio, null,
            r.Followers, r.Following, r.Works, r.Chapters,
            viewerId is null || viewerId == p.UserId ? null : r.Followed, onboardingComplete,
            p.City, p.Discipline, p.Availability, p.Website, p.Social, p.Studio, p.ReplyTime, p.PublicEmail,
            p.Services.Select(s => new ServiceDto(s.Title, s.Desc, s.Price)).ToList(),
            p.Shows.Select(s => new ShowDto(s.Year, s.Text)).ToList());
    }
}
