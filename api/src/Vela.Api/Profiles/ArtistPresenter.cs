using Vela.Api.Images;

namespace Vela.Api.Profiles;

/// <summary>Turns query rows into public profile DTOs with avatar URLs, for lists that embed profiles.</summary>
public sealed class ArtistPresenter(ArtistQueries artists, IBlobStore blobs)
{
    public async Task<List<ArtistDto>> PresentAsync(IReadOnlyList<Guid> ids, Guid? viewerId) =>
        (await artists.ByIdsAsync(ids, viewerId)).Select(r =>
        {
            var dto = ArtistQueries.ToDto(r, viewerId);
            return r.Profile.AvatarBlob is { Length: > 0 } a ? dto with { AvatarUrl = blobs.PublicUrl(a).ToString() } : dto;
        }).ToList();
}
