using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;
using Vela.Api.Data.Entities;

namespace Vela.Api.Admin;

/// <summary>Reads the content, its owner and the reports about it into a snapshot. Raw sign-in subjects are never included.</summary>
public sealed class EvidenceSnapshotBuilder(VelaDbContext db, TimeProvider clock)
{
    public const int Version = 1;

    public async Task<EvidenceSnapshot> BuildAsync(
        string targetType, Guid ownerId, Work? work, Reply? reply, bool originalCopied, IReadOnlyList<string> files)
    {
        var owner = await OwnerAsync(ownerId, includeProfile: targetType == "user");
        SnapshotWork? w = null;
        SnapshotReply? r = null;
        if (work is not null)
        {
            var chapters = await db.Chapters.AsNoTracking().Where(c => c.WorkId == work.Id).OrderBy(c => c.Index)
                .Select(c => new SnapshotChapter(c.Index, c.X, c.Y, c.Zoom, c.Title, c.Body, c.Meta, c.AltText)).ToListAsync();
            w = new SnapshotWork(work.Id, work.Title, work.Intro, work.Status.ToString().ToLowerInvariant(), work.AltText,
                work.Width, work.Height, work.CreatedAt, work.PublishedAt, chapters);
        }
        if (reply is not null)
            r = new SnapshotReply(reply.Id, reply.WorkId, reply.ChapterIndex, reply.AuthorId, owner.Handle, reply.Text, reply.Hidden, reply.CreatedAt);

        var targetId = work?.Id ?? reply?.Id ?? ownerId;
        var reports = await db.Reports.AsNoTracking().Where(x => x.TargetType == targetType && x.TargetId == targetId)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new SnapshotReport(x.Id, x.Source, x.Reason, x.Status, x.CreatedAt)).ToListAsync();
        return new EvidenceSnapshot(Version, targetType, clock.GetUtcNow(), owner, w, r, reports, originalCopied, files);
    }

    private async Task<SnapshotOwner> OwnerAsync(Guid userId, bool includeProfile)
    {
        var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId);
        var identities = await db.Identities.AsNoTracking().Where(i => i.UserId == userId).OrderBy(i => i.Provider).ThenBy(i => i.Id)
            .Select(i => new SnapshotIdentity(i.Provider, i.Email)).ToListAsync();
        var detail = includeProfile && profile is not null
            ? new SnapshotProfile(profile.DisplayName, profile.Bio, profile.City, profile.Discipline, profile.Availability, profile.Website,
                profile.Social, profile.Studio, profile.ReplyTime, profile.PublicEmail)
            : null;
        return new SnapshotOwner(userId, profile?.Handle, profile?.DisplayName, identities, detail);
    }
}
