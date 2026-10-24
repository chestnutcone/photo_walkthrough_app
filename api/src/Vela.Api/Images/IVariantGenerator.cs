namespace Vela.Api.Images;

public interface IVariantGenerator
{
    /// <summary>
    /// Reads <paramref name="sourceBlob"/> and writes thumb, feed and full WebP variants into works/{workId}/. Runs inside the
    /// process-wide decode gate (throws <see cref="ImageBusyException"/> when no slot frees in time). Never deletes the source:
    /// callers decide (a draft upload is removed on failure, a sealed original never is).
    /// </summary>
    Task<ImageVariants> GenerateAsync(string sourceBlob, Guid workId);

    /// <summary>Source is originals/avatars/{userId}; it is deleted on success and on <see cref="InvalidImageException"/> (not when busy). Writes "avatars/{userId}/avatar-256.webp" (centre-cropped square) and returns its blob name.</summary>
    Task<string> GenerateAvatarAsync(string sourceBlob, Guid userId);
}
