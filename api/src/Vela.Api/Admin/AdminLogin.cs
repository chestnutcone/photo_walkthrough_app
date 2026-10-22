using Microsoft.EntityFrameworkCore;
using Vela.Api.Auth;
using Vela.Api.Auth.EmailCodes;
using Vela.Api.Auth.Identity;
using Vela.Api.Common;
using Vela.Api.Data;

namespace Vela.Api.Admin;

public sealed record AdminSignIn(Guid UserId, string Email);

/// <summary>
/// Admin sign-in through the existing email-code flow, restricted to the allow-list. Admin status is granted here and
/// nowhere else. The API tokens the verify call issues are revoked at once (only that session family).
/// </summary>
public sealed class AdminLoginService(IAdminAllowList allow, IEmailCodeService codes, ITokenService tokens, VelaDbContext db)
{
    /// <summary>Sends a code only to an allow-listed address. Every other outcome is silent, so callers cannot tell them apart.</summary>
    public async Task RequestCodeAsync(string? email, string ip)
    {
        if (EmailCodeService.Normalize(email) is not { } normalized || !allow.Contains(normalized)) return;
        try { await codes.RequestAsync(normalized, ip); }
        catch (Exception e) when (e is RateLimitedException or InvalidEmailException) { }
    }

    /// <summary>Null for every failure; the caller shows one generic message.</summary>
    public async Task<AdminSignIn?> VerifyAsync(string? email, string? code, string ip)
    {
        // An outsider's code is never burned at this door.
        if (EmailCodeService.Normalize(email) is not { } normalized || !allow.Contains(normalized)) return null;
        EmailSignInResult result;
        try { result = await codes.VerifyAsync(normalized, code ?? "", ip); }
        catch (Exception e) when (e is InvalidCodeException or RateLimitedException or AccountSuspendedException or AccountDeletedException)
        {
            return null;
        }

        await tokens.RevokeAsync(result.Pair.RefreshToken);
        if (!allow.Contains(normalized)) return null;
        await db.Users.Where(u => u.Id == result.UserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsAdmin, true));
        return new AdminSignIn(result.UserId, normalized);
    }
}
