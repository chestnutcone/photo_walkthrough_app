using Microsoft.EntityFrameworkCore;
using Vela.Api.Auth.EmailCodes;
using Vela.Api.Data;
using Vela.Api.Images;

namespace Vela.Api.Account;

/// <summary>
/// Hard-deletes one account marked <c>DeletedAt</c>: blobs, CDN purge, Apple revocation, then every database row in one
/// transaction. Idempotent and resumable: each step is safe to repeat and a user that no longer exists is a no-op.
/// The completeness test scans the live schema, so a new user-referencing table or column fails there until it is handled here.
/// </summary>
public sealed class DeletionJob(
    VelaDbContext db, IBlobStore blobs, ICdnPurger cdn, IAppleTokenRevoker apple, ILogger<DeletionJob> log)
{
    private const string Savepoint = "account_deletion_rows";

    public async Task RunAsync(Guid userId, CancellationToken ct)
    {
        var state = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.DeletedAt }).SingleOrDefaultAsync(ct);
        if (state is null) return;
        if (state.DeletedAt is null)
        {
            log.LogWarning("Hard delete refused for {UserId}: the account is not marked deleted.", userId);
            return;
        }

        var workIds = await db.Works.AsNoTracking().Where(w => w.ArtistId == userId).Select(w => w.Id).ToListAsync(ct);
        var prefixes = await DeleteBlobsAsync(userId, workIds);
        await PurgeCdnAsync(userId, workIds);
        await RevokeAppleAsync(userId, ct);

        var rows = await DeleteRowsAsync(userId, workIds, ct);
        if (rows.LateWorkIds.Count > 0)
        {
            // Works created after step 1 started: their blobs go too, and so do their cached copies.
            await DeleteBlobsAsync(userId, rows.LateWorkIds);
            await PurgeCdnAsync(userId, rows.LateWorkIds);
        }
        log.LogInformation("Account {UserId} hard-deleted: {Works} works, {BlobPrefixes} blob prefixes, {Rows} rows.",
            userId, workIds.Count + rows.LateWorkIds.Count, prefixes, rows.Deleted);
    }

    private async Task<int> DeleteBlobsAsync(Guid userId, IReadOnlyList<Guid> workIds)
    {
        var prefixes = new List<string>();
        foreach (var id in workIds) prefixes.AddRange([BlobNames.WorkVariantsPrefix(id), BlobNames.WorkOriginal(id)]);
        prefixes.AddRange([BlobNames.AvatarVariantsPrefix(userId), BlobNames.AvatarOriginal(userId), BlobNames.ExportsPrefix(userId)]);
        foreach (var prefix in prefixes) await blobs.DeletePrefixAsync(prefix);
        return prefixes.Count;
    }

    /// <summary>Only the public variants are cached at the edge; originals and exports are private and never CDN-fronted.</summary>
    private async Task PurgeCdnAsync(Guid userId, IReadOnlyList<Guid> workIds)
    {
        var urls = workIds.Select(id => blobs.PublicUrl(BlobNames.WorkVariantsPrefix(id)).ToString())
            .Append(blobs.PublicUrl(BlobNames.AvatarVariantsPrefix(userId)).ToString()).ToList();
        await cdn.PurgeAsync(urls);
    }

    private async Task RevokeAppleAsync(Guid userId, CancellationToken ct)
    {
        var identities = await db.Identities.AsNoTracking().Where(i => i.UserId == userId && i.Provider == "apple").ToListAsync(ct);
        foreach (var identity in identities) await apple.RevokeAsync(identity);
    }

    private sealed record RowResult(int Deleted, IReadOnlyList<Guid> LateWorkIds);

    /// <summary>Joins the caller's transaction (the sweep holds the claim lock in one) through a savepoint, else opens its own.</summary>
    private async Task<RowResult> DeleteRowsAsync(Guid userId, IReadOnlyList<Guid> handledWorks, CancellationToken ct)
    {
        var outer = db.Database.CurrentTransaction;
        await using var own = outer is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var tx = outer ?? own!;
        if (outer is not null) await tx.CreateSavepointAsync(Savepoint, ct);
        try
        {
            var result = await DeleteRowsInTransactionAsync(userId, handledWorks, ct);
            if (own is not null) await own.CommitAsync(ct);
            return result;
        }
        catch
        {
            if (outer is not null) await outer.RollbackToSavepointAsync(Savepoint, CancellationToken.None);
            throw;
        }
    }

    private async Task<RowResult> DeleteRowsInTransactionAsync(Guid userId, IReadOnlyList<Guid> handledWorks, CancellationToken ct)
    {
        var locked = await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM users WHERE id = {userId} FOR UPDATE").ToListAsync(ct);
        if (locked.Count == 0) return new RowResult(0, []);

        var works = db.Works.Where(w => w.ArtistId == userId).Select(w => w.Id);
        var replies = db.Replies.Where(r => r.AuthorId == userId || works.Contains(r.WorkId)).Select(r => r.Id);
        var loginEmails = (await db.Identities.AsNoTracking().Where(i => i.UserId == userId && i.Email != null).Select(i => i.Email!).ToListAsync(ct))
            .Select(EmailCodeService.Normalize).OfType<string>().Distinct().ToList();
        var lateWorks = (await works.ToListAsync(ct)).Except(handledWorks).ToList();
        var deleted = 0;

        // Not foreign keys: they point at the user's content (or the user) by id, so cascades never reach them.
        deleted += await db.Notifications.Where(n => n.UserId == userId || n.ActorId == userId
            || (n.WorkId != null && works.Contains(n.WorkId.Value)) || (n.ReplyId != null && replies.Contains(n.ReplyId.Value))).ExecuteDeleteAsync(ct);
        deleted += await EvidenceHoldGuard.Deletable(db.Reports).Where(r =>
            (r.TargetType == "user" && r.TargetId == userId) || (r.TargetType == "work" && works.Contains(r.TargetId))
            || (r.TargetType == "reply" && replies.Contains(r.TargetId))).ExecuteDeleteAsync(ct);
        // Reports the user filed about others stay for moderation, anonymised.
        await db.Reports.Where(r => r.ReporterId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReporterId, (Guid?)null).SetProperty(r => r.Note, (string?)null), ct);
        deleted += await db.Inquiries.Where(i => i.SenderId == userId || i.ArtistId == userId
            || (i.WorkId != null && works.Contains(i.WorkId.Value))).ExecuteDeleteAsync(ct);
        deleted += await db.EmailCodes.Where(c => loginEmails.Contains(c.Email)).ExecuteDeleteAsync(ct);
        deleted += await db.VerifyAttempts.Where(a => loginEmails.Contains(a.Email)).ExecuteDeleteAsync(ct);

        // Foreign-keyed rows, deleted explicitly rather than trusting the cascade alone.
        deleted += await db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
        deleted += await db.Identities.Where(i => i.UserId == userId).ExecuteDeleteAsync(ct);
        deleted += await db.Follows.Where(f => f.FollowerId == userId || f.FolloweeId == userId).ExecuteDeleteAsync(ct);
        deleted += await db.Blocks.Where(b => b.BlockerId == userId || b.BlockedId == userId).ExecuteDeleteAsync(ct);
        deleted += await db.Replies.Where(r => r.AuthorId == userId || works.Contains(r.WorkId)).ExecuteDeleteAsync(ct);
        deleted += await db.Chapters.Where(c => works.Contains(c.WorkId)).ExecuteDeleteAsync(ct);
        deleted += await db.Works.Where(w => w.ArtistId == userId).ExecuteDeleteAsync(ct);
        deleted += await db.ExportJobs.Where(j => j.UserId == userId).ExecuteDeleteAsync(ct);
        deleted += await db.Profiles.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);
        deleted += await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);
        return new RowResult(deleted, lateWorks);
    }
}
