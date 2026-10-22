using Microsoft.EntityFrameworkCore;
using Npgsql;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Images;
using Vela.Api.Works;

namespace Vela.Api.Profiles;

public sealed class ProfileService(VelaDbContext db, ArtistQueries artists, WorkCounts workCounts, TimeProvider clock,
    IBlobStore blobs, IVariantGenerator images) : IProfileService
{
    private static readonly TimeSpan HandleChangeWindow = TimeSpan.FromDays(30);

    public async Task<MeDto> GetMeAsync(Guid userId)
    {
        var user = await ActiveUserAsync(userId, track: false);
        var tier = await db.Tiers.AsNoTracking().SingleAsync(t => t.Id == user.TierId);
        var row = await artists.FindOwnAsync(userId);
        var onboarded = row is not null && user.AgeConfirmedAt is not null && user.TermsAcceptedAt is not null;
        return new MeDto(userId, row is null ? null : WithAvatar(ArtistQueries.ToDto(row, userId, onboarded), row.Profile.AvatarBlob),
            onboarded, new TierDto(tier.Name, tier.MaxWorks), await workCounts.CountActiveAsync(userId));
    }

    public async Task CompleteOnboardingAsync(Guid userId, OnboardingRequest req)
    {
        if (!req.AgeConfirmed || !req.TermsAccepted)
            throw new ProfileException(400, "age_and_terms_required", "You must confirm you are 18 or older and accept the terms.");
        var name = ProfileValidator.DisplayName(req.DisplayName);
        var handle = HandleRules.Normalize(req.Handle);
        if (!HandleRules.IsValid(handle)) throw InvalidHandle();
        var wanted = req.FollowArtistIds.Distinct().Where(id => id != userId).ToList();
        if (req.FollowArtistIds.Count > ProfileValidator.MaxFollowIds)
            throw ProfileException.Validation("followArtistIds", $"allows at most {ProfileValidator.MaxFollowIds} ids.");

        await using var tx = await db.Database.BeginTransactionAsync();
        var user = await ActiveUserAsync(userId, track: true);
        if (user.AgeConfirmedAt is not null || await db.Profiles.AnyAsync(p => p.UserId == userId)) throw AlreadyOnboarded();
        if (await db.Profiles.AnyAsync(p => p.Handle == handle)) throw HandleTaken();

        var now = clock.GetUtcNow();
        user.AgeConfirmedAt = now;
        user.TermsAcceptedAt = now;
        db.Profiles.Add(new Profile { UserId = userId, Handle = handle, DisplayName = name });
        foreach (var id in await FollowableAsync(wanted))
            db.Follows.Add(new Follow { FollowerId = userId, FolloweeId = id, CreatedAt = now });
        await SaveAsync();
        await tx.CommitAsync();
    }

    public async Task UpdateAsync(Guid userId, UpdateProfileRequest req)
    {
        var user = await ActiveUserAsync(userId, track: false);
        var profile = await db.Profiles.SingleOrDefaultAsync(p => p.UserId == userId);
        if (profile is null || user.AgeConfirmedAt is null || user.TermsAcceptedAt is null)
            throw new ProfileException(409, "onboarding_required", "Finish onboarding first.");
        ProfileValidator.ValidateUpdate(req);
        await ApplyHandleAsync(profile, req.Handle);
        Apply(profile, req);
        if (req.AvatarUploaded) profile.AvatarBlob = await GenerateAvatarAsync(userId);
        await SaveAsync();
    }

    public async Task<ArtistDto?> GetByHandleAsync(string handle, Guid? viewerId) =>
        await artists.FindByHandleAsync(handle, viewerId) is { } row ? WithAvatar(ArtistQueries.ToDto(row, viewerId), row.Profile.AvatarBlob) : null;

    public async Task<IReadOnlyList<ArtistDto>> GetSuggestedAsync(Guid? viewerId) =>
        (await artists.SuggestedAsync(viewerId)).Select(r => WithAvatar(ArtistQueries.ToDto(r, viewerId), r.Profile.AvatarBlob)).ToList();

    private ArtistDto WithAvatar(ArtistDto dto, string? blob) =>
        blob is null ? dto : dto with { AvatarUrl = blobs.PublicUrl(blob).ToString() };

    private async Task<string> GenerateAvatarAsync(Guid userId)
    {
        try { return await images.GenerateAvatarAsync(BlobNames.AvatarOriginal(userId), userId); }
        catch (InvalidImageException)
        {
            throw new ProfileException(400, "invalid_image", "That image can't be used. Use a JPEG, PNG or WebP under 25 MB.");
        }
    }

    private async Task ApplyHandleAsync(Profile profile, string? requested)
    {
        if (requested is null) return;
        var handle = HandleRules.Normalize(requested);
        if (!HandleRules.IsValid(handle)) throw InvalidHandle();
        if (handle == profile.Handle) return;
        var now = clock.GetUtcNow();
        if (profile.HandleChangedAt is { } at && now - at < HandleChangeWindow)
            throw new ProfileException(409, "handle_change_too_soon", "You can change your handle once every 30 days.");
        if (await db.Profiles.AnyAsync(p => p.Handle == handle)) throw HandleTaken();
        profile.Handle = handle;
        profile.HandleChangedAt = now;
    }

    private static void Apply(Profile p, UpdateProfileRequest r)
    {
        p.DisplayName = r.DisplayName is null ? p.DisplayName : r.DisplayName.Trim();
        p.Bio = r.Bio ?? p.Bio;
        p.City = r.City ?? p.City;
        p.Discipline = r.Discipline ?? p.Discipline;
        p.Availability = r.Availability ?? p.Availability;
        p.Website = r.Website ?? p.Website;
        p.Social = r.Social ?? p.Social;
        p.Studio = r.Studio ?? p.Studio;
        p.ReplyTime = r.ReplyTime ?? p.ReplyTime;
        if (r.ClearPublicEmail) p.PublicEmail = null;
        else if (r.PublicEmail is not null) p.PublicEmail = r.PublicEmail.Length == 0 ? null : r.PublicEmail;
        if (r.Services is not null) p.Services = r.Services.Select(s => new ServiceItem(s.Title, s.Description, s.Price)).ToList();
        if (r.Shows is not null) p.Shows = r.Shows.Select(s => new ShowItem(s.Year, s.Text)).ToList();
    }

    /// <summary>Ids that exist, are onboarded and active; unknown, deleted and half-signed-up ids are dropped.</summary>
    private Task<List<Guid>> FollowableAsync(List<Guid> ids) =>
        db.Profiles.Where(p => ids.Contains(p.UserId) && db.Users.Any(u => u.Id == p.UserId
                && u.DeletedAt == null && u.SuspendedAt == null && u.AgeConfirmedAt != null && u.TermsAcceptedAt != null))
            .Select(p => p.UserId).ToListAsync();

    private async Task<User> ActiveUserAsync(Guid userId, bool track)
    {
        var q = track ? db.Users : db.Users.AsNoTracking();
        var user = await q.SingleOrDefaultAsync(u => u.Id == userId);
        if (user is null || user.DeletedAt is not null) throw new ProfileException(401, "unauthorized", "Sign in to continue.");
        if (user.SuspendedAt is not null) throw new ProfileException(403, "account_suspended", "This account is suspended.");
        return user;
    }

    private async Task SaveAsync()
    {
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            // Lost a race: another request took the handle (or finished this user's onboarding) first.
            throw pg.ConstraintName?.Contains("handle", StringComparison.OrdinalIgnoreCase) == true ? HandleTaken() : AlreadyOnboarded();
        }
    }

    private static ProfileException InvalidHandle() =>
        new(400, "invalid_handle", "Handles are 3-20 characters: lowercase letters, digits and underscores.");
    private static ProfileException HandleTaken() => new(409, "handle_taken", "That handle is already taken.");
    private static ProfileException AlreadyOnboarded() => new(409, "already_onboarded", "Onboarding is already complete.");
}
