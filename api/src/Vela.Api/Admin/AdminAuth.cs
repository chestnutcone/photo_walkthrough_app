using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;

namespace Vela.Api.Admin;

/// <summary>
/// Cookie sign-in for the admin pages. Separate scheme and policy from the JSON API's bearer tokens: the policy names
/// only this scheme, and the cookie is scoped to <c>/admin</c>.
/// </summary>
public static class AdminAuth
{
    public const string Scheme = "AdminCookie";
    public const string Policy = "AdminCookie";
    public const string CookieName = "vela_admin";
    public const string AdminClaim = "vela_admin";
    public const string EmailClaim = "vela_email";
    public const string StampClaim = "vela_stamp";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    public static CookieSecurePolicy SecurePolicy(IHostEnvironment env) =>
        env.EnvironmentName is "Development" or "Test" ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

    public static Guid? UserId(ClaimsPrincipal? p) => Guid.TryParse(p?.FindFirstValue("sub"), out var id) ? id : null;

    public static ClaimsPrincipal CreatePrincipal(Guid userId, string email, Guid stamp) =>
        new(new ClaimsIdentity([new Claim("sub", userId.ToString()), new Claim(EmailClaim, email), new Claim(AdminClaim, "1"), new Claim(StampClaim, stamp.ToString())],
            Scheme, "sub", ClaimTypes.Role));

    public static AuthenticationProperties SessionProperties(DateTimeOffset now) =>
        new() { IsPersistent = false, AllowRefresh = false, IssuedUtc = now, ExpiresUtc = now + SessionLifetime };

    public static void ConfigureCookie(CookieAuthenticationOptions o, IHostEnvironment env)
    {
        o.Cookie.Name = CookieName;
        o.Cookie.Path = "/admin";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = SecurePolicy(env);
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.ExpireTimeSpan = SessionLifetime;
        o.SlidingExpiration = false;
        o.LoginPath = "/admin/login";
        o.AccessDeniedPath = "/admin/login";
        o.EventsType = typeof(AdminCookieEvents);
    }
}

/// <summary>Re-checks the account on every admin request, so demoting, suspending or deleting an admin ends their session.</summary>
public sealed class AdminSessionValidator(VelaDbContext db, IAdminAllowList allow)
{
    public async Task<bool> IsValidAsync(ClaimsPrincipal? principal)
    {
        var id = AdminAuth.UserId(principal);
        var email = principal?.FindFirstValue(AdminAuth.EmailClaim);
        if (id is null || email is null || principal!.FindFirstValue(AdminAuth.AdminClaim) != "1") return false;
        if (!Guid.TryParse(principal.FindFirstValue(AdminAuth.StampClaim), out var stamp)) return false;
        if (!allow.Contains(email))
        {
            // Removed from the allow-list: the admin flag goes too, not just this session.
            await db.Users.Where(u => u.Id == id && u.IsAdmin).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsAdmin, false));
            return false;
        }
        // The stamp makes the cookie a single live session: signing out (or signing in again) invalidates every copy of the old one.
        return await db.Users.AsNoTracking().AnyAsync(u => u.Id == id && u.IsAdmin && u.DeletedAt == null && u.SuspendedAt == null
            && u.AdminSessionStamp == stamp
            && db.Identities.Any(i => i.UserId == u.Id && i.Provider == "email" && i.Subject == email));
    }
}

public sealed class AdminCookieEvents : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var validator = context.HttpContext.RequestServices.GetRequiredService<AdminSessionValidator>();
        if (await validator.IsValidAsync(context.Principal)) return;
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(AdminAuth.Scheme);
    }

    // No return-url round trip: nothing for an attacker to redirect through.
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.Redirect("/admin/login");
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
