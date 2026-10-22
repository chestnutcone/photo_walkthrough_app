namespace Vela.Api.Images;

public interface IVariantGenerator
{
    /// <summary>Reads <paramref name="sourceBlob"/> ("originals/works/{id}") and writes thumb, feed and full WebP variants into works/{id}/.</summary>
    Task<ImageVariants> GenerateAsync(string sourceBlob);

    /// <summary>Source is originals/avatars/{userId}; on success the source is deleted. Writes "avatars/{userId}/avatar-256.webp" (centre-cropped square) and returns its blob name.</summary>
    Task<string> GenerateAvatarAsync(string sourceBlob, Guid userId);
}
