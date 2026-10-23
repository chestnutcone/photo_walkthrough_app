using Microsoft.EntityFrameworkCore;
using Vela.Api.Auth;
using Vela.Api.Common;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Images;

namespace Vela.Api.Admin;

/// <summary>
/// Every admin decision. Each action checks the acting admin, needs a reason, changes state, resolves the pending reports
/// on the same target together, and writes one audit row, all in one transaction.
/// </summary>
public sealed partial class ModerationService(
    VelaDbContext db, TimeProvider clock, IWorkVariantLifecycle lifecycle, ITokenService tokens, IDomainEventBus bus, AdminSettings settings, IUserStateCache stateCache)
    : IModerationService
{
    public const int MaxReasonLength = 500;
    private const string DefaultDismissReason = "dismissed";

    public async Task<ModerationOutcome> DismissReportAsync(Guid adminId, Guid reportId, string? reason = null)
    {
        var text = string.IsNullOrWhiteSpace(reason) ? DefaultDismissReason : reason;
        if (await GateAsync(adminId, text) is { } refused) return refused;
        var report = await db.Reports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reportId);
        if (report is null) return ModerationOutcome.NotFound;
        if (report.Status != ReportStatus.Pending.ToDb()) return ModerationOutcome.AlreadyResolved;
        // Dismissing must never leave auto-blocked content blocked with nothing left in the queue.
        if (await IsAutoBlockedAsync(report.TargetType, report.TargetId)) return ModerationOutcome.TargetStillBlocked;

        await using var tx = await db.Database.BeginTransactionAsync();
        await ResolveAsync(report.TargetType, report.TargetId, ReportStatus.Dismissed);
        await UnhideReportHiddenAsync(report.TargetType, report.TargetId);
        Audit(adminId, "dismiss", "report", reportId, text.Trim());
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return ModerationOutcome.Ok;
    }

    public async Task<ModerationOutcome> RestoreAsync(Guid adminId, Guid reportId, string reason)
    {
        if (await GateAsync(adminId, reason) is { } refused) return refused;
        var report = await db.Reports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reportId);
        if (report is null) return ModerationOutcome.NotFound;
        return report.TargetType switch
        {
            "work" => await RestoreWorkAsync(adminId, report.TargetId, reason.Trim()),
            "reply" => await RestoreReplyAsync(adminId, report.TargetId, reason.Trim()),
            _ => ModerationOutcome.NotApplicable,
        };
    }

    private async Task<ModerationOutcome> RestoreWorkAsync(Guid adminId, Guid workId, string reason)
    {
        var work = await db.Works.AsNoTracking().SingleOrDefaultAsync(w => w.Id == workId);
        if (work is null) return ModerationOutcome.NotFound;
        var republish = work.Status is WorkStatus.Blocked or WorkStatus.Removed;
        var regenerated = false;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            if (republish)
            {
                try { await lifecycle.RestoreAsync(workId); regenerated = true; }
                catch (Exception e) when (e is FileNotFoundException or InvalidImageException) { return ModerationOutcome.VariantsUnavailable; }
                var now = clock.GetUtcNow();
                var publishedAt = work.PublishedAt ?? now;
                await db.Works.Where(w => w.Id == workId).ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.Status, WorkStatus.Published).SetProperty(w => w.ModerationOverride, true)
                    .SetProperty(w => w.PublishedAt, publishedAt).SetProperty(w => w.ReportHidden, false));
                await bus.PublishAsync(new WorkPublished(workId, work.ArtistId), CancellationToken.None);
            }
            else if (work.ReportHidden)
                await db.Works.Where(w => w.Id == workId).ExecuteUpdateAsync(s => s.SetProperty(w => w.ReportHidden, false));
            await ResolveAsync("work", workId, ReportStatus.Actioned);
            Audit(adminId, "restore", "work", workId, reason);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return ModerationOutcome.Ok;
        }
        catch when (regenerated)
        {
            await lifecycle.TakeDownAsync(workId); // never leave blocked content reachable after a failed restore
            throw;
        }
    }

    private async Task<ModerationOutcome> RestoreReplyAsync(Guid adminId, Guid replyId, string reason)
    {
        if (!await db.Replies.AnyAsync(r => r.Id == replyId)) return ModerationOutcome.NotFound;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Replies.Where(r => r.Id == replyId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Hidden, false).SetProperty(r => r.HiddenBy, (HiddenBy?)null));
        await ResolveAsync("reply", replyId, ReportStatus.Actioned);
        Audit(adminId, "restore", "reply", replyId, reason);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return ModerationOutcome.Ok;
    }

    public async Task<ModerationOutcome> HideReplyAsync(Guid adminId, Guid replyId, string reason)
    {
        if (await GateAsync(adminId, reason) is { } refused) return refused;
        if (!await db.Replies.AnyAsync(r => r.Id == replyId)) return ModerationOutcome.NotFound;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Replies.Where(r => r.Id == replyId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Hidden, true).SetProperty(r => r.HiddenBy, (HiddenBy?)HiddenBy.Admin));
        await ResolveAsync("reply", replyId, ReportStatus.Actioned);
        Audit(adminId, "hide_reply", "reply", replyId, reason.Trim());
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return ModerationOutcome.Ok;
    }

    public async Task<ModerationOutcome> RemoveWorkAsync(Guid adminId, Guid workId, string reason)
    {
        if (await GateAsync(adminId, reason) is { } refused) return refused;
        var status = await db.Works.AsNoTracking().Where(w => w.Id == workId).Select(w => (WorkStatus?)w.Status).SingleOrDefaultAsync();
        if (status is null) return ModerationOutcome.NotFound;
        if (status == WorkStatus.Draft) return ModerationOutcome.NotApplicable;

        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Works.Where(w => w.Id == workId).ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkStatus.Removed));
        await ResolveAsync("work", workId, ReportStatus.Actioned);
        Audit(adminId, "remove_work", "work", workId, reason.Trim());
        await db.SaveChangesAsync();
        await lifecycle.TakeDownAsync(workId); // before commit: a failure rolls the removal back so it can be retried
        await tx.CommitAsync();
        return ModerationOutcome.Ok;
    }

    public async Task<ModerationOutcome> SuspendUserAsync(Guid adminId, Guid userId, string reason)
    {
        if (await GateAsync(adminId, reason) is { } refused) return refused;
        if (userId == adminId) return ModerationOutcome.CannotBanSelf;
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId && u.DeletedAt == null)
            .Select(u => new { u.IsAdmin }).SingleOrDefaultAsync();
        if (user is null) return ModerationOutcome.NotFound;
        if (user.IsAdmin) return ModerationOutcome.CannotBanAdmin;
        if (settings.BanPepper.Length == 0) throw new InvalidOperationException("Admin:BanPepper is not configured.");

        var now = clock.GetUtcNow();
        var identities = await db.Identities.AsNoTracking().Where(i => i.UserId == userId).Select(i => new { i.Provider, i.Subject }).ToListAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Users.Where(u => u.Id == userId && u.SuspendedAt == null).ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, now));
        await tokens.RevokeAllAsync(userId);
        await BanIdentitiesAsync(userId, identities.Select(i => (i.Provider, i.Subject)), now);
        await ResolveAsync("user", userId, ReportStatus.Actioned);
        Audit(adminId, "ban_user", "user", userId, reason.Trim());
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        stateCache.Evict(userId);
        return ModerationOutcome.Ok;
    }

    private async Task BanIdentitiesAsync(Guid userId, IEnumerable<(string Provider, string Subject)> identities, DateTimeOffset now)
    {
        var wanted = identities.Select(i => (i.Provider, Hash: BanHasher.Hash(settings.BanPepper, i.Provider, i.Subject))).Distinct().ToList();
        var hashes = wanted.Select(w => w.Hash).ToList();
        var existing = (await db.BannedIdentities.AsNoTracking().Where(b => hashes.Contains(b.SubjectHash)).ToListAsync())
            .Select(b => (b.Provider, b.SubjectHash)).ToHashSet();
        foreach (var (provider, hash) in wanted.Where(w => !existing.Contains((w.Provider, w.Hash))))
            db.BannedIdentities.Add(new BannedIdentity { Provider = provider, SubjectHash = hash, UserRef = userId, CreatedAt = now });
    }

    public async Task<ModerationOutcome> ReinstateUserAsync(Guid adminId, Guid userId, string reason)
    {
        if (await GateAsync(adminId, reason) is { } refused) return refused;
        var exists = await db.Users.AnyAsync(u => u.Id == userId);
        // A deleted account has no user row, but its ban rows can still be lifted by id.
        if (!exists && !await db.BannedIdentities.AnyAsync(b => b.UserRef == userId)) return ModerationOutcome.NotFound;

        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.SuspendedAt, (DateTimeOffset?)null));
        await db.BannedIdentities.Where(b => b.UserRef == userId).ExecuteDeleteAsync();
        Audit(adminId, "reinstate_user", "user", userId, reason.Trim());
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        stateCache.Evict(userId);
        return ModerationOutcome.Ok;
    }

    // ---- shared pieces ----

    /// <summary>The acting admin must exist, be an admin and be in good standing; the reason must be 1-500 characters.</summary>
    private async Task<ModerationOutcome?> GateAsync(Guid adminId, string? reason)
    {
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == adminId && u.IsAdmin && u.DeletedAt == null && u.SuspendedAt == null))
            return ModerationOutcome.Forbidden;
        var trimmed = reason?.Trim() ?? "";
        if (trimmed.Length == 0) return ModerationOutcome.ReasonRequired;
        return trimmed.Length > MaxReasonLength ? ModerationOutcome.ReasonTooLong : null;
    }

    private void Audit(Guid adminId, string action, string targetType, Guid targetId, string reason) =>
        db.AdminActions.Add(new AdminAction
        {
            Id = Guid.NewGuid(), AdminId = adminId, Action = action, TargetType = targetType, TargetId = targetId,
            Reason = reason, At = clock.GetUtcNow(),
        });

    /// <summary>Every pending report on the same target is resolved together.</summary>
    private Task<int> ResolveAsync(string targetType, Guid targetId, ReportStatus status)
    {
        var value = status.ToDb();
        var pending = ReportStatus.Pending.ToDb();
        return db.Reports.Where(r => r.TargetType == targetType && r.TargetId == targetId && r.Status == pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, value));
    }

    private async Task<bool> IsAutoBlockedAsync(string targetType, Guid targetId) => targetType switch
    {
        "work" => await db.Works.AnyAsync(w => w.Id == targetId && w.Status == WorkStatus.Blocked),
        "reply" => await db.Replies.AnyAsync(r => r.Id == targetId && r.Hidden && r.HiddenBy == HiddenBy.Auto),
        _ => false,
    };

    private async Task UnhideReportHiddenAsync(string targetType, Guid targetId)
    {
        if (targetType == "work")
            await db.Works.Where(w => w.Id == targetId && w.ReportHidden).ExecuteUpdateAsync(s => s.SetProperty(w => w.ReportHidden, false));
        else if (targetType == "reply")
            await db.Replies.Where(r => r.Id == targetId && r.Hidden && r.HiddenBy == HiddenBy.Report)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Hidden, false).SetProperty(r => r.HiddenBy, (HiddenBy?)null));
    }
}
