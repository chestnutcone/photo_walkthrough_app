using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;
using Vela.Api.Notifications;

namespace Vela.Api.Account;

// Field names here are the export's public format: change them only deliberately.
public sealed record ServiceDoc(string Title, string Description, string Price);
public sealed record ShowDoc(string Year, string Text);
public sealed record ProfileDoc(
    string Handle, DateTimeOffset? HandleChangedAt, string DisplayName, string Bio, string City, string Discipline, string Availability,
    string Website, string Social, string Studio, string ReplyTime, string? PublicEmail, IReadOnlyList<ServiceDoc> Services, IReadOnlyList<ShowDoc> Shows);
public sealed record IdentityDoc(string Provider, string? Email);
public sealed record AccountDoc(
    Guid UserId, DateTimeOffset CreatedAt, string Tier, DateTimeOffset? AgeConfirmedAt, DateTimeOffset? TermsAcceptedAt, IReadOnlyList<IdentityDoc> Identities);
public sealed record ChapterDoc(int Index, double X, double Y, double? Zoom, string Title, string Body, string? Meta, string? AltText);
public sealed record WorkDoc(
    Guid Id, string Status, string Title, string Intro, string? AltText, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt, IReadOnlyList<ChapterDoc> Chapters);
public sealed record ReplyDoc(Guid WorkId, int ChapterIndex, string Text, DateTimeOffset CreatedAt, bool Hidden);
public sealed record FollowsDoc(IReadOnlyList<string> Following, IReadOnlyList<string> Followers);
public sealed record BlocksDoc(IReadOnlyList<string> Blocked);
public sealed record NotificationDoc(string Type, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
public sealed record InquirySentDoc(string Type, string? ArtistHandle, string Message, string ReplyEmail, DateTimeOffset CreatedAt);
public sealed record InquiriesReceivedSummaryDoc(int Count);
/// <summary>The fact of a copyright notice about the user's own story: reference id, status and dates only. Never the claimant's details.</summary>
public sealed record DmcaNoticeRefDoc(
    Guid NoticeId, Guid? WorkId, string Status, DateTimeOffset ReceivedAt, DateTimeOffset? TakedownAt, DateTimeOffset? CounterNoticeAt, DateTimeOffset? ForwardedAt);
public sealed record ReportFiledDoc(string TargetType, string Reason, string? Note, DateTimeOffset CreatedAt);

/// <summary>Reads one user's own data. It never selects other users' emails, messages, tokens, codes or IP addresses.</summary>
public sealed class ExportData(VelaDbContext db)
{
    public async Task<ProfileDoc?> ProfileAsync(Guid userId) =>
        (await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId)) is { } p
            ? new ProfileDoc(p.Handle, p.HandleChangedAt, p.DisplayName, p.Bio, p.City, p.Discipline, p.Availability, p.Website, p.Social, p.Studio,
                p.ReplyTime, p.PublicEmail, [.. p.Services.Select(s => new ServiceDoc(s.Title, s.Desc, s.Price))], [.. p.Shows.Select(s => new ShowDoc(s.Year, s.Text))])
            : null;

    public async Task<AccountDoc> AccountAsync(Guid userId)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        var tier = await db.Tiers.AsNoTracking().Where(t => t.Id == user.TierId).Select(t => t.Name).SingleAsync();
        var identities = await db.Identities.AsNoTracking().Where(i => i.UserId == userId).OrderBy(i => i.Provider).ThenBy(i => i.Id)
            .Select(i => new IdentityDoc(i.Provider, i.Email)).ToListAsync();
        return new AccountDoc(user.Id, user.CreatedAt, tier, user.AgeConfirmedAt, user.TermsAcceptedAt, identities);
    }

    public async Task<List<WorkDoc>> WorksAsync(Guid userId)
    {
        var works = await db.Works.AsNoTracking().Where(w => w.ArtistId == userId).OrderBy(w => w.CreatedAt).ThenBy(w => w.Id).ToListAsync();
        var ids = works.Select(w => w.Id).ToList();
        var chapters = (await db.Chapters.AsNoTracking().Where(c => ids.Contains(c.WorkId)).OrderBy(c => c.Index).ToListAsync())
            .ToLookup(c => c.WorkId, c => new ChapterDoc(c.Index, c.X, c.Y, c.Zoom, c.Title, c.Body, c.Meta, c.AltText));
        return [.. works.Select(w => new WorkDoc(w.Id, w.Status.ToString().ToLowerInvariant(), w.Title, w.Intro, w.AltText, w.CreatedAt, w.PublishedAt, [.. chapters[w.Id]]))];
    }

    public async Task<List<ReplyDoc>> RepliesAsync(Guid userId) =>
        await db.Replies.AsNoTracking().Where(r => r.AuthorId == userId).OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Select(r => new ReplyDoc(r.WorkId, r.ChapterIndex, r.Text, r.CreatedAt, r.Hidden)).ToListAsync();

    public async Task<FollowsDoc> FollowsAsync(Guid userId)
    {
        var following = await (from f in db.Follows.AsNoTracking() join p in db.Profiles on f.FolloweeId equals p.UserId where f.FollowerId == userId orderby p.Handle select p.Handle).ToListAsync();
        var followers = await (from f in db.Follows.AsNoTracking() join p in db.Profiles on f.FollowerId equals p.UserId where f.FolloweeId == userId orderby p.Handle select p.Handle).ToListAsync();
        return new FollowsDoc(following, followers);
    }

    public async Task<BlocksDoc> BlocksAsync(Guid userId) =>
        new(await (from b in db.Blocks.AsNoTracking() join p in db.Profiles on b.BlockedId equals p.UserId where b.BlockerId == userId orderby p.Handle select p.Handle).ToListAsync());

    public async Task<List<NotificationDoc>> NotificationsAsync(Guid userId) =>
        (await db.Notifications.AsNoTracking().Where(n => n.UserId == userId).OrderBy(n => n.CreatedAt).ThenBy(n => n.Id).ToListAsync())
            .Select(n => new NotificationDoc(NotificationTypes.ToContract(n.Type), n.CreatedAt, n.ReadAt)).ToList();

    public async Task<List<InquirySentDoc>> InquiriesSentAsync(Guid userId) =>
        await (from i in db.Inquiries.AsNoTracking()
               join p in db.Profiles on i.ArtistId equals p.UserId into artists
               from artist in artists.DefaultIfEmpty()
               where i.SenderId == userId orderby i.CreatedAt, i.Id
               select new InquirySentDoc(i.Type, artist == null ? null : artist.Handle, i.Message, i.SenderEmail, i.CreatedAt)).ToListAsync();

    /// <summary>A count only: the senders' details and messages belong to them.</summary>
    public async Task<InquiriesReceivedSummaryDoc> InquiriesReceivedAsync(Guid userId) =>
        new(await db.Inquiries.AsNoTracking().CountAsync(i => i.ArtistId == userId));

    public async Task<List<ReportFiledDoc>> ReportsFiledAsync(Guid userId) =>
        await db.Reports.AsNoTracking().Where(r => r.ReporterId == userId).OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Select(r => new ReportFiledDoc(r.TargetType, r.Reason, r.Note, r.CreatedAt)).ToListAsync();

    public async Task<List<DmcaNoticeRefDoc>> DmcaNoticesAboutMyWorksAsync(Guid userId) =>
        await db.DmcaNotices.AsNoTracking().Where(n => n.OwnerUserId == userId).OrderBy(n => n.CreatedAt).ThenBy(n => n.Id)
            .Select(n => new DmcaNoticeRefDoc(n.Id, n.WorkId, n.Status, n.CreatedAt, n.TakedownAt, n.CounterAt, n.ForwardedAt)).ToListAsync();
}
