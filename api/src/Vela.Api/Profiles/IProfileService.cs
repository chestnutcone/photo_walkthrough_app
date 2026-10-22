namespace Vela.Api.Profiles;

public interface IProfileService
{
    Task<MeDto> GetMeAsync(Guid userId);
    Task CompleteOnboardingAsync(Guid userId, OnboardingRequest req);
    Task UpdateAsync(Guid userId, UpdateProfileRequest req);
    /// <summary>Public lookup. <paramref name="viewerId"/> drives isFollowing and (task A6) block-based hiding.</summary>
    Task<ArtistDto?> GetByHandleAsync(string handle, Guid? viewerId);
    Task<IReadOnlyList<ArtistDto>> GetSuggestedAsync(Guid? viewerId);
}
