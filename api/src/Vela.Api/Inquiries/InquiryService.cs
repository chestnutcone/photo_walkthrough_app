using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vela.Api.Branding;
using Vela.Api.Common;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Profiles;
using Vela.Api.Social;
using Vela.Api.Works;

namespace Vela.Api.Inquiries;

public sealed record InquiryRequest(string Type, string Email, string Message, Guid? WorkId);

/// <summary>The sender's own copy. Never carries the artist's login email.</summary>
public sealed record InquiryDto(Guid Id, Guid ArtistId, string Type, Guid? WorkId, string Message, DateTimeOffset CreatedAt);

public interface IInquiryService
{
    Task<InquiryDto> SendAsync(Guid senderId, Guid artistId, InquiryRequest req, IPAddress? clientIp = null);
}

public sealed class InquiryService(
    VelaDbContext db, TimeProvider clock, IEmailSender email, IVisibility visibility, ArtistQueries artists,
    IOptions<BrandingOptions> branding, ILogger<InquiryService> log) : IInquiryService
{
    public const int MaxPerSenderPerDay = 10;
    public const int MaxPerSenderPerArtistPerDay = 3;
    public const int MaxPerIpPerDay = 30;
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public static SafetyException ArtistNotFound() => new(StatusCodes.Status404NotFound, "artist_not_found", "No such artist.");

    public async Task<InquiryDto> SendAsync(Guid senderId, Guid artistId, InquiryRequest req, IPAddress? clientIp = null)
    {
        req = InquiryValidator.Validate(req);
        if (senderId == artistId)
            throw new SafetyException(StatusCodes.Status400BadRequest, "cannot_inquire_self", "You cannot send an inquiry to yourself.");
        if (!await artists.IsVisibleAsync(artistId, senderId)) throw ArtistNotFound();
        var workTitle = await WorkTitleAsync(senderId, artistId, req.WorkId);

        var inquiry = await SaveAsync(senderId, artistId, req, clientIp?.ToString());
        await NotifyArtistAsync(inquiry, workTitle);
        return new InquiryDto(inquiry.Id, artistId, inquiry.Type, inquiry.WorkId, inquiry.Message, inquiry.CreatedAt);
    }

    private async Task<string?> WorkTitleAsync(Guid senderId, Guid artistId, Guid? workId)
    {
        if (workId is not { } id) return null;
        var hidden = await visibility.HiddenUserIdsAsync(senderId);
        return await WorkVisibility.Public(db, hidden).Where(w => w.Id == id && w.ArtistId == artistId).Select(w => w.Title).FirstOrDefaultAsync()
            ?? throw InquiryValidator.Invalid("workId must be a published story by this artist.");
    }

    /// <summary>Counts and inserts in one transaction under a per-sender lock, so parallel requests cannot slip past the limits.</summary>
    private async Task<Inquiry> SaveAsync(Guid senderId, Guid artistId, InquiryRequest req, string? ip)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended('inquiry:' || {senderId.ToString()}, 0))");
        var since = now - Window;
        var recent = db.Inquiries.AsNoTracking().Where(i => i.CreatedAt > since);
        var waits = new[]
        {
            RateLimiting.RetryAfter(await Newest(recent.Where(i => i.SenderId == senderId), MaxPerSenderPerDay), MaxPerSenderPerDay, Window, now),
            RateLimiting.RetryAfter(await Newest(recent.Where(i => i.SenderId == senderId && i.ArtistId == artistId), MaxPerSenderPerArtistPerDay), MaxPerSenderPerArtistPerDay, Window, now),
            ip is null ? null : RateLimiting.RetryAfter(await Newest(recent.Where(i => i.ClientIp == ip), MaxPerIpPerDay), MaxPerIpPerDay, Window, now),
        }.OfType<TimeSpan>().ToList();
        if (waits.Count > 0)
            throw new SafetyException(StatusCodes.Status429TooManyRequests, "rate_limited", "Too many inquiries. Try again later.", waits.Max());

        var inquiry = new Inquiry
        {
            Id = Guid.NewGuid(), SenderId = senderId, ArtistId = artistId, Type = req.Type, SenderEmail = req.Email,
            WorkId = req.WorkId, Message = req.Message, ClientIp = ip, CreatedAt = now,
        };
        db.Inquiries.Add(inquiry);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return inquiry;
    }

    private static async Task<IReadOnlyList<DateTimeOffset>> Newest(IQueryable<Inquiry> q, int limit) =>
        await q.OrderByDescending(i => i.CreatedAt).Take(limit).Select(i => i.CreatedAt).ToListAsync();

    /// <summary>Best effort after the row is stored: a failure leaves EmailedAt null and is logged as a reason code only.</summary>
    private async Task NotifyArtistAsync(Inquiry inquiry, string? workTitle)
    {
        try
        {
            var to = await Auth.LoginEmail.ForUserAsync(db, inquiry.ArtistId);
            if (to is null) { log.LogInformation("Inquiry email skipped ({Reason}).", "no_recipient"); return; }

            await email.SendAsync(to, $"New {inquiry.Type} inquiry via {branding.Value.AppName}", Body(inquiry, workTitle));
            await db.Inquiries.Where(i => i.Id == inquiry.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.EmailedAt, clock.GetUtcNow()));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning("Inquiry email failed ({Reason}).", e.GetType().Name);
        }
    }

    /// <summary>Plain text only; the message goes in verbatim and nothing user-written reaches the subject.</summary>
    private static string Body(Inquiry i, string? workTitle)
    {
        var about = workTitle is null ? "" : $"About: {workTitle}\n";
        return $"Type: {i.Type}\nReply to: {i.SenderEmail}\n{about}\nMessage:\n{i.Message}\n";
    }
}
