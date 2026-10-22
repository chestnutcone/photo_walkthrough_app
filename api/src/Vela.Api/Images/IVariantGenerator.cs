namespace Vela.Api.Images;

public interface IVariantGenerator
{
    /// <summary>Reads <paramref name="sourceBlob"/> ("works/{id}/original") and writes thumb, feed and full WebP variants beside it.</summary>
    Task<ImageVariants> GenerateAsync(string sourceBlob);

    /// <summary>Writes "avatars/{userId}/avatar-256.webp" (centre-cropped square) and returns its blob name.</summary>
    Task<string> GenerateAvatarAsync(string sourceBlob, Guid userId);
}
