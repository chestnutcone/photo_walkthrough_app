using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Vela.Api.Data;
using Vela.Api.Data.Entities;

namespace Vela.Api.Auth;

public sealed class TokenService(VelaDbContext db, IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);
    private const int RefreshTokenBytes = 32;

    private readonly JwtOptions _jwt = options.Value;

    public Task<TokenPair> IssueAsync(Guid userId) => CreateSessionTokenAsync(userId, Guid.NewGuid());

    public async Task<TokenPair> RefreshAsync(string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken)) throw new InvalidRefreshTokenException();
        var now = clock.GetUtcNow();
        var hash = Hash(refreshToken);
        var row = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash)
                  ?? throw new InvalidRefreshTokenException();

        if (row.RevokedAt is not null) throw new InvalidRefreshTokenException();
        if (row.RotatedAt is not null)
        {
            // A rotated token is being replayed: assume theft and kill the whole session.
            await RevokeFamilyAsync(row.FamilyId, now);
            throw new InvalidRefreshTokenException();
        }
        if (row.ExpiresAt <= now) throw new InvalidRefreshTokenException();

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == row.UserId);
        if (user is null || user.DeletedAt is not null || user.SuspendedAt is not null)
        {
            await RevokeFamilyAsync(row.FamilyId, now);
            throw new InvalidRefreshTokenException();
        }

        // Atomic claim: of concurrent refreshes with the same token only one wins.
        var claimed = await db.RefreshTokens
            .Where(t => t.Id == row.Id && t.RotatedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RotatedAt, now));
        if (claimed == 0)
        {
            await RevokeFamilyAsync(row.FamilyId, now);
            throw new InvalidRefreshTokenException();
        }
        return await CreateSessionTokenAsync(row.UserId, row.FamilyId);
    }

    public async Task RevokeAsync(string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken)) return;
        var hash = Hash(refreshToken);
        var familyId = await db.RefreshTokens.Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.FamilyId).SingleOrDefaultAsync();
        if (familyId is { } id) await RevokeFamilyAsync(id, clock.GetUtcNow());
    }

    public Task RevokeAllAsync(Guid userId)
    {
        var now = clock.GetUtcNow();
        return db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
    }

    private Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now) =>
        db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));

    private async Task<TokenPair> CreateSessionTokenAsync(Guid userId, Guid familyId)
    {
        var now = clock.GetUtcNow();
        var raw = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(RefreshTokenBytes));
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = userId, FamilyId = familyId,
            TokenHash = Hash(raw), CreatedAt = now, ExpiresAt = now + RefreshLifetime,
        });
        await db.SaveChangesAsync();

        var expires = now + AccessLifetime;
        var access = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity([new("sub", userId.ToString()), new("jti", Guid.NewGuid().ToString("N"))]),
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(_jwt.SecurityKey, SecurityAlgorithms.HmacSha256),
        });
        return new TokenPair(access, raw, expires);
    }

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
