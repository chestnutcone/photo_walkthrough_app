namespace Vela.Api.Images;

public interface IVariantGenerator
{
    /// <summary>
    /// Reads <paramref name="sourceBlob"/> and writes thumb, feed and full WebP variants into works/{workId}/. Runs inside the
    /// process-wide decode gate (throws <see cref="ImageBusyException"/> when no slot frees in time). Never deletes the source:
    /// callers decide (a draft upload is removed on failure, a sealed original never is).
    /// </summary>
    Task<ImageVariants> GenerateAsync(string sourceBlob, Guid workId);

    /// <summary>
    /// Source is originals/avatars/{userId}; it is deleted on success, on <see cref="InvalidImageException"/> and when
    /// <paramref name="accept"/> returns false (then <see cref="UnsafeImageException"/>), but not when busy or when
    /// <paramref name="accept"/> throws. The encoded square is handed to <paramref name="accept"/> (outside the decode gate)
    /// before anything is written, so a refused image never reaches the public container. Writes
    /// "avatars/{userId}/avatar-256.webp" (centre-cropped square) and returns its blob name.
    /// </summary>
    Task<string> GenerateAvatarAsync(string sourceBlob, Guid userId, Func<Stream, Task<bool>>? accept = null);
}
