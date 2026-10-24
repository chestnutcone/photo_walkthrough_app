using Microsoft.EntityFrameworkCore;
using Vela.Api.Auth;
using Vela.Api.Auth.EmailCodes;
using Vela.Api.Auth.Identity;
using Vela.Api.Common;
using Vela.Api.Data;
using Vela.Api.Data.Entities;

namespace Vela.Api.Admin;

public sealed record AdminSignIn(Guid UserId, string Email, Guid Stamp);

/// <summary>
/// Admin sign-in through the existing email-code flow, restricted to the allow-list. Admin status is granted here and
/// nowhere else. The API tokens the verify call issues are revoked at once (only that session family).
/// </summary>
public sealed class AdminLoginService(IAdminAllowList allow, IEmailCodeService codes, ITokenService tokens, VelaDbContext db, AdminSettings settings, TimeProvider clock)
{
    /// <summary>Sends a code only to an allow-listed address. Every other outcome is silent, so callers cannot tell them apart.</summary>
    public Task RequestCodeAsync(string? email, string ip) =>
        WithFloorAsync(async () => { await RequestCodeCoreAsync(email, ip); return true; });

    private async Task RequestCodeCoreAsync(string? email, string ip)
    {
        if (EmailCodeService.Normalize(email) is not { } normalized || !allow.Contains(normalized)) return;
        try { await codes.RequestAsync(normalized, ip); }
        catch (Exception e) when (e is RateLimitedException or InvalidEmailException) { }
    }

    /// <summary>Null for every failure; the caller shows one generic message.</summary>
    public Task<AdminSignIn?> VerifyAsync(string? email, string? code, string ip) => WithFloorAsync(() => VerifyCoreAsync(email, code, ip));

    /// <summary>Allow-listed addresses do real work (database, mail) and others return at once; padding every answer to the same minimum hides the difference.</summary>
    private async Task<T> WithFloorAsync<T>(Func<Task<T>> work)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        try { return await work(); }
        finally
        {
            var remaining = settings.LoginFloor - started.Elapsed;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
        }
    }

    private async Task<AdminSignIn?> VerifyCoreAsync(string? email, string? code, string ip)
    {
        // An outsider's code is never burned at this door.
        if (EmailCodeService.Normalize(email) is not { } normalized) return null;
        if (!allow.Contains(normalized))
        {
            await ClearStaleAdminAsync(normalized);
            return null;
        }
        EmailSignInResult result;
        try { result = await codes.VerifyAsync(normalized, code ?? "", ip); }
        catch (Exception e) when (e is InvalidCodeException or RateLimitedException or AccountSuspendedException or AccountDeletedException)
        {
            return null;
        }

        await tokens.RevokeAsync(result.Pair.RefreshToken);
        if (!allow.Contains(normalized))
        {
            await ClearStaleAdminAsync(normalized);
            return null;
        }
        var stamp = Guid.NewGuid();
        await db.Users.Where(u => u.Id == result.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsAdmin, true).SetProperty(u => u.AdminSessionStamp, stamp));
        await AuditAsync(result.UserId, "admin_login", "email-code sign-in");
        return new AdminSignIn(result.UserId, normalized, stamp);
    }

    /// <summary>An admin whose address left the allow-list loses the flag the next time it is seen here.</summary>
    private Task<int> ClearStaleAdminAsync(string normalizedEmail) =>
        db.Users.Where(u => u.IsAdmin && db.Identities.Any(i => i.UserId == u.Id && i.Provider == "email" && i.Subject == normalizedEmail))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsAdmin, false));

    /// <summary>Ends the one live session: every copy of the cookie is refused from the next request on.</summary>
    public async Task SignOutAsync(Guid userId)
    {
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.AdminSessionStamp, (Guid?)null));
        await AuditAsync(userId, "admin_logout", "sign-out");
    }

    /// <summary>Signing in or out is an admin action like any other, so it goes in the audit trail (target: the session of that admin).</summary>
    private async Task AuditAsync(Guid adminId, string action, string reason)
    {
        db.AdminActions.Add(new AdminAction
        {
            Id = Guid.NewGuid(), AdminId = adminId, Action = action, TargetType = "session", TargetId = adminId, Reason = reason, At = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync();
    }
}
