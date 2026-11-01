using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;

namespace Vela.Api.Auth;

/// <summary>Picks the address that service mail (inquiry alerts, export notices) goes to: the email-code login first, else the oldest-created identity id.</summary>
public static class LoginEmail
{
    public static Task<string?> ForUserAsync(VelaDbContext db, Guid userId) =>
        db.Identities.AsNoTracking().Where(i => i.UserId == userId && i.Email != null)
            .OrderBy(i => i.Provider == "email" ? 0 : 1).ThenBy(i => i.Id).Select(i => i.Email).FirstOrDefaultAsync();
}
