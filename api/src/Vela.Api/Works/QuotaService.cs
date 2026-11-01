using Microsoft.EntityFrameworkCore;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Images;
using Vela.Api.Profiles;

namespace Vela.Api.Works;

public sealed class QuotaExceededException(int limit) : Exception($"Story limit reached ({limit}).")
{
    public int Limit { get; } = limit;
}

public interface IQuotaService
{
    /// <summary>Creates a draft inside a transaction that serialises concurrent calls for the same user.</summary>
    Task<Work> CreateDraftAsync(Guid userId, string? title = null);
}

public sealed class QuotaService(VelaDbContext db, WorkCounts counts, TimeProvider clock) : IQuotaService
{
    public static readonly TimeSpan DraftLifetime = TimeSpan.FromHours(24);

    public async Task<Work> CreateDraftAsync(Guid userId, string? title = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        // The row lock makes a second draft request for this user wait until the first commits.
        var user = (await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id = {userId} FOR UPDATE").ToListAsync()).SingleOrDefault();
        if (user is null || user.DeletedAt is not null) throw new ProfileException(401, "unauthorized", "Sign in to continue.");
        if (user.SuspendedAt is not null) throw new ProfileException(403, "account_suspended", "This account is suspended.");
        if (user.AgeConfirmedAt is null || user.TermsAcceptedAt is null || !await db.Profiles.AnyAsync(p => p.UserId == userId))
            throw new ProfileException(409, "onboarding_required", "Finish onboarding first.");

        var limit = (await db.Tiers.AsNoTracking().SingleAsync(t => t.Id == user.TierId)).MaxWorks;
        if (await counts.CountActiveAsync(userId) >= limit) throw new QuotaExceededException(limit);

        var now = clock.GetUtcNow();
        var id = Guid.NewGuid();
        var work = new Work
        {
            Id = id, ArtistId = userId, Title = title?.Trim() ?? "", Status = WorkStatus.Draft,
            ImageBlob = BlobNames.WorkOriginal(id), CreatedAt = now, ExpiresAt = now + DraftLifetime,
        };
        db.Works.Add(work);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return work;
    }
}
