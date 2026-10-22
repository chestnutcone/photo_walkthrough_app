using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;
using Vela.Api.Data.Entities;

namespace Vela.Api.Admin;

public static class BanHasher
{
    /// <summary>HMAC-SHA256(pepper, provider + ":" + subject) as lowercase hex. Email subjects are lower-cased first.</summary>
    public static string Hash(string pepper, string provider, string subject)
    {
        var normalized = provider == "email" ? subject.Trim().ToLowerInvariant() : subject.Trim();
        return Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(pepper), Encoding.UTF8.GetBytes(provider + ":" + normalized)));
    }
}

/// <summary>Banned identities (hash only). Consulted before a new account is created.</summary>
public interface IBanList
{
    Task<bool> IsBannedAsync(string provider, string subject);
}

public sealed class BanList(VelaDbContext db, AdminSettings settings) : IBanList
{
    public async Task<bool> IsBannedAsync(string provider, string subject)
    {
        // Without a pepper nothing can have been banned (startup requires one whenever the admin area is on).
        if (settings.BanPepper.Length == 0) return false;
        var hash = BanHasher.Hash(settings.BanPepper, provider, subject);
        return await db.BannedIdentities.AsNoTracking().AnyAsync(b => b.Provider == provider && b.SubjectHash == hash);
    }
}
