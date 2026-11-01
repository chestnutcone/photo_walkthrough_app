namespace Vela.Api.Profiles;

public sealed record ServiceDto(string Title, string Description, string Price);

public sealed record ShowDto(string Year, string Text);

/// <summary>Public artist view. Never carries the login email or identities; PublicEmail is the optional contact address.</summary>
public sealed record ArtistDto(
    Guid Id, string Handle, string DisplayName, string Bio, string? AvatarUrl,
    int FollowerCount, int FollowingCount, int WorkCount, int ChapterCount, bool? IsFollowing, bool OnboardingComplete,
    string City, string Discipline, string Availability, string Website, string Social, string Studio, string ReplyTime,
    string? PublicEmail, IReadOnlyList<ServiceDto> Services, IReadOnlyList<ShowDto> Shows);

public sealed record TierDto(string Name, int MaxWorks);

public sealed record MeDto(Guid Id, ArtistDto? Profile, bool OnboardingComplete, TierDto Tier, int UsedWorks);

public sealed record OnboardingRequest(string DisplayName, string Handle, bool AgeConfirmed, bool TermsAccepted, IReadOnlyList<Guid> FollowArtistIds);

/// <summary>Partial update: null means "not provided" (ClearPublicEmail is the explicit null for publicEmail).</summary>
public sealed record UpdateProfileRequest
{
    public string? DisplayName { get; init; }
    public string? Handle { get; init; }
    public string? Bio { get; init; }
    public string? City { get; init; }
    public string? Discipline { get; init; }
    public string? Availability { get; init; }
    public string? Website { get; init; }
    public string? Social { get; init; }
    public string? Studio { get; init; }
    public string? ReplyTime { get; init; }
    public string? PublicEmail { get; init; }
    public bool ClearPublicEmail { get; init; }
    /// <summary>The client has uploaded originals/avatars/{userId}; generate the avatar from it.</summary>
    public bool AvatarUploaded { get; init; }
    public IReadOnlyList<ServiceDto>? Services { get; init; }
    public IReadOnlyList<ShowDto>? Shows { get; init; }
}
