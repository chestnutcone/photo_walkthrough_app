using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Auth.Identity;
using Vela.Api.Common;
using Vela.Api.Data;
using Vela.Api.Data.Entities;

namespace Vela.Api.Auth.EmailCodes;

public sealed class EmailCodeService(
    VelaDbContext db, IEmailSender sender, IAccountResolver resolver, ITokenService tokens,
    TimeProvider clock, EmailCodeSettings settings, ILogger<EmailCodeService> log) : IEmailCodeService
{
    public const int MaxEmailLength = 254;
    public const int MaxAttempts = 5;
    public const int MaxRequestsPerEmailPerHour = 5;
    public const int MaxRequestsPerIpPerHour = 20;
    public const int MaxVerifiesPerIpPerHour = 30;
    public const int MaxReviewFailuresPerHour = 5;
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    public async Task RequestAsync(string email, string ip)
    {
        var normalized = Normalize(email) ?? throw new InvalidEmailException();
        var now = clock.GetUtcNow();
        await EnforceRateLimitsAsync(normalized, ip, now);

        // A review address never gets mail: it signs in with the fixed code.
        if (settings.IsReviewEmail(normalized)) return;

        var code = GenerateCode();
        await db.EmailCodes.Where(c => c.Email == normalized && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now));
        db.EmailCodes.Add(new EmailCode
        {
            Id = Guid.NewGuid(), Email = normalized, CodeHash = Hash(normalized, code), RequestIp = ip,
            CreatedAt = now, ExpiresAt = now + CodeLifetime,
        });
        await db.SaveChangesAsync();

        try
        {
            await sender.SendAsync(normalized, "Your sign-in code",
                $"Your sign-in code is {code}.\n\nIt expires in 10 minutes. If you did not request it, you can ignore this email.");
        }
        catch (Exception e)
        {
            // Same response as success so account existence is never revealed. No address, no code in the log.
            log.LogError("Email code delivery failed. reason={Reason}", $"send_failed:{e.GetType().Name}");
        }
    }

    public async Task<EmailSignInResult> VerifyAsync(string email, string code, string ip)
    {
        await EnforceVerifyIpLimitAsync(ip);
        var normalized = Normalize(email) ?? throw new InvalidCodeException();
        if (!await CodeAcceptedAsync(normalized, code ?? "")) throw new InvalidCodeException();

        var (userId, isNew) = await resolver.SignInAsync(new VerifiedIdentity("email", normalized, normalized, true));
        return new EmailSignInResult(await tokens.IssueAsync(userId), userId, isNew);
    }

    private async Task<bool> CodeAcceptedAsync(string email, string code)
    {
        if (settings.IsReviewEmail(email))
            return await ReviewCodeAcceptedAsync(email, code);

        var now = clock.GetUtcNow();
        var row = await db.EmailCodes.AsNoTracking()
            .Where(c => c.Email == email && c.ConsumedAt == null && c.ExpiresAt > now)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync();
        if (row is null) return false;

        // Count the attempt atomically before comparing: the sixth attempt is refused even when correct.
        var claimed = await db.EmailCodes
            .Where(c => c.Id == row.Id && c.ConsumedAt == null && c.Attempts < MaxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1));
        if (claimed == 0) return false;

        if (!FixedTimeEquals(Hash(email, code), row.CodeHash)) return false;

        // Single-use: only one concurrent verifier wins the consume.
        var consumed = await db.EmailCodes.Where(c => c.Id == row.Id && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now));
        return consumed == 1;
    }

    /// <summary>
    /// Five wrong guesses per email per hour lock the fixed code (even the correct one). A per-email advisory
    /// lock serialises concurrent guesses so the limit cannot be exceeded.
    /// </summary>
    private async Task<bool> ReviewCodeAcceptedAsync(string email, string code)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({"review:" + email}))");
        var since = now - Window;
        var failures = await db.VerifyAttempts.CountAsync(a => a.Email == email && a.Kind == VerifyAttempt.ReviewFailure && a.CreatedAt > since);
        if (failures >= MaxReviewFailuresPerHour) return false;
        if (FixedTimeEquals(code, settings.ReviewCode)) return true;

        db.VerifyAttempts.Add(new VerifyAttempt
        {
            Id = Guid.NewGuid(), Email = email, Kind = VerifyAttempt.ReviewFailure, CreatedAt = now,
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return false;
    }

    private async Task EnforceVerifyIpLimitAsync(string ip)
    {
        var now = clock.GetUtcNow();
        var since = now - Window;
        var hits = await db.VerifyAttempts.AsNoTracking()
            .Where(a => a.Ip == ip && a.Kind == VerifyAttempt.Verify && a.CreatedAt > since)
            .Select(a => a.CreatedAt).ToListAsync();
        if (RateLimiting.RetryAfter(hits, MaxVerifiesPerIpPerHour, Window, now) is { } wait)
            throw new RateLimitedException(wait);
        db.VerifyAttempts.Add(new VerifyAttempt { Id = Guid.NewGuid(), Ip = ip, Kind = VerifyAttempt.Verify, CreatedAt = now });
        await db.SaveChangesAsync();
    }

    private async Task EnforceRateLimitsAsync(string email, string ip, DateTimeOffset now)
    {
        var since = now - Window;
        var byEmail = await db.EmailCodes.AsNoTracking().Where(c => c.Email == email && c.CreatedAt > since)
            .Select(c => c.CreatedAt).ToListAsync();
        var byIp = await db.EmailCodes.AsNoTracking().Where(c => c.RequestIp == ip && c.CreatedAt > since)
            .Select(c => c.CreatedAt).ToListAsync();
        var waits = new[]
        {
            RateLimiting.RetryAfter(byEmail, MaxRequestsPerEmailPerHour, Window, now),
            RateLimiting.RetryAfter(byIp, MaxRequestsPerIpPerHour, Window, now),
        }.Where(w => w is not null).ToList();
        if (waits.Count > 0) throw new RateLimitedException(waits.Max()!.Value);
    }

    /// <summary>Trim + lower-case; null when not exactly one '@' with non-empty parts or over 254 chars.</summary>
    internal static string? Normalize(string? email)
    {
        var e = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(e) || e.Length > MaxEmailLength) return null;
        var parts = e.Split('@');
        return parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0 ? e : null;
    }

    private static string GenerateCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private string Hash(string email, string code) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.Pepper), Encoding.UTF8.GetBytes($"{email}:{code}")));

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
