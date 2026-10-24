using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vela.Api.Branding;
using Vela.Api.Common;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Social;

namespace Vela.Api.Legal;

public interface IDmcaService
{
    /// <summary>Stores the notice and returns its id. Whether or not the reference matched a work, the caller sees the same answer.</summary>
    Task<Guid> SubmitAsync(DmcaRequest request, IPAddress? clientIp);
}

public sealed class DmcaService(
    VelaDbContext db, TimeProvider clock, IEmailSender email, IOptions<BrandingOptions> branding, ILogger<DmcaService> log) : IDmcaService
{
    public const int MaxPerIpPerDay = 5;
    /// <summary>Acknowledgement emails to one claimant address per day. Notices are still stored beyond this; only the mail stops, so the form cannot be used to bomb someone else's inbox.</summary>
    public const int MaxAcksPerEmailPerDay = 3;
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);
    public const string AckSubject = "DMCA notice received";
    public const string ReportSource = "dmca";
    public const string ReportReason = "copyright";

    private static readonly HashSet<string> GmailDomains = new(StringComparer.Ordinal) { "gmail.com", "googlemail.com" };

    /// <summary>
    /// One key per real inbox (S1b-5): lower case, any "+tag" removed, and for Gmail the dots in the local part (which Gmail
    /// ignores) removed and googlemail.com folded into gmail.com. Used only to count acknowledgements, never to send.
    /// </summary>
    public static string Mailbox(string email)
    {
        var lower = email.Trim().ToLowerInvariant();
        var at = lower.LastIndexOf('@');
        if (at <= 0) return lower;
        var (local, domain) = (lower[..at], lower[(at + 1)..]);
        if (local.IndexOf('+') is var plus and > 0) local = local[..plus];
        if (GmailDomains.Contains(domain)) (local, domain) = (local.Replace(".", ""), "gmail.com");
        return $"{local}@{domain}";
    }

    public async Task<Guid> SubmitAsync(DmcaRequest req, IPAddress? clientIp)
    {
        // S1b-5: the per-address limit counts an IPv6 /64 as one address, like the in-memory limiters.
        var ip = clientIp is null ? null : Abuse.IpKey.Of(clientIp);
        var work = DmcaValidator.WorkIdFrom(req.WorkReference) is { } id
            ? await db.Works.AsNoTracking().Where(w => w.Id == id && w.Status != WorkStatus.Draft)
                .Select(w => new { w.Id, w.ArtistId }).SingleOrDefaultAsync()
            : null;
        var notice = await SaveAsync(req, ip, work?.Id, work?.ArtistId);
        if (await AckAllowedAsync(notice)) await AcknowledgeAsync(notice);
        return notice.Id;
    }

    /// <summary>Limit check, notice and (for a match) the queue entry, in one transaction under a per-address lock.</summary>
    private async Task<DmcaNotice> SaveAsync(DmcaRequest req, string? ip, Guid? workId, Guid? ownerId)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended('dmca:' || {ip ?? ""}, 0))");
        if (ip is not null)
        {
            var since = now - Window;
            var hits = await db.DmcaNotices.AsNoTracking().Where(n => n.ClientIp == ip && n.CreatedAt > since)
                .OrderByDescending(n => n.CreatedAt).Take(MaxPerIpPerDay).Select(n => n.CreatedAt).ToListAsync();
            if (RateLimiting.RetryAfter(hits, MaxPerIpPerDay, Window, now) is { } wait)
                throw new SafetyException(StatusCodes.Status429TooManyRequests, "rate_limited", "Too many notices from this address. Try again later.", wait);
        }

        var notice = new DmcaNotice
        {
            Id = Guid.NewGuid(), WorkId = workId, OwnerUserId = ownerId, ClaimantName = req.ClaimantName, ClaimantEmail = req.ClaimantEmail,
            ClaimantAddress = req.ClaimantAddress, ClaimantPhone = req.ClaimantPhone, WorkReference = req.WorkReference,
            OriginalWorkDescription = req.OriginalWorkDescription, SignatureName = req.SignatureName,
            GoodFaithStatement = true, PerjuryStatement = true, Status = DmcaStatus.Pending, CreatedAt = now, ClientIp = ip,
            ClaimantMailbox = Mailbox(req.ClaimantEmail),
        };
        db.DmcaNotices.Add(notice);
        if (workId is { } id && !await db.Reports.AnyAsync(r => r.Source == ReportSource && r.TargetType == "work" && r.TargetId == id && r.Status == ReportService.Pending))
            db.Reports.Add(new Report
            {
                Id = Guid.NewGuid(), Source = ReportSource, Reason = ReportReason, Status = ReportService.Pending,
                TargetType = "work", TargetId = id, ReporterId = null, CreatedAt = now,
            });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return notice;
    }

    private async Task<bool> AckAllowedAsync(DmcaNotice notice)
    {
        var since = notice.CreatedAt - Window;
        var mailbox = notice.ClaimantMailbox;
        var recent = await db.DmcaNotices.AsNoTracking().CountAsync(n => n.ClaimantMailbox == mailbox && n.CreatedAt > since);
        return recent <= MaxAcksPerEmailPerDay; // this notice is already stored, so it is counted
    }

    /// <summary>Best effort: a fixed subject and a body with only the reference id and generic next steps. Nothing the claimant typed.</summary>
    private async Task AcknowledgeAsync(DmcaNotice notice)
    {
        try
        {
            var app = branding.Value.AppName;
            await email.SendAsync(notice.ClaimantEmail, AckSubject,
                $"We received your copyright notice.\n\nReference: {notice.Id}\n\nWhat happens next: our team reviews each notice. " +
                $"If it is complete and the story is on {app}, the story may be taken down and its owner told how to respond. " +
                "You do not need to send the notice again. Please quote the reference above in any follow-up.\n");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning("DMCA acknowledgement email failed ({Reason}).", e.GetType().Name);
        }
    }
}
