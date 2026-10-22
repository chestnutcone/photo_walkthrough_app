namespace Vela.Api.Images;

/// <summary>
/// Blob naming. Raw uploads (which keep EXIF/GPS) live only in the private "originals" container;
/// public containers ("works", "avatars") hold pixel-only variants.
/// </summary>
public static class BlobNames
{
    public const string Originals = "originals";
    public const string Works = "works";
    public const string Avatars = "avatars";
    /// <summary>Private container for data-export archives; never CDN-fronted.</summary>
    public const string Exports = "exports";

    public static string WorkOriginal(Guid workId) => $"{Originals}/{Works}/{workId}";
    public static string AvatarOriginal(Guid userId) => $"{Originals}/{Avatars}/{userId}";
    /// <summary>Prefix of a work's public variants; delete together with <see cref="WorkOriginal"/>.</summary>
    public static string WorkVariantsPrefix(Guid workId) => $"{Works}/{workId}/";
    public static string ExportsPrefix(Guid userId) => $"{Exports}/{userId}/";
    public static string ExportArchive(Guid userId, Guid exportId) => $"{Exports}/{userId}/{exportId}.zip";
    public static string AvatarVariantsPrefix(Guid userId) => $"{Avatars}/{userId}/";
}
