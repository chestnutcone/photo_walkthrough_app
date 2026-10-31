namespace Vela.Api.Images;

/// <summary>
/// Content-type checks from the bytes themselves (S2-4). The uploader declares a type twice (createDraft's <c>contentType</c> and
/// the <c>Content-Type</c> of its PUT, which the blob keeps); the stored declaration must name the format the bytes actually are.
/// </summary>
public static class ImageSniffer
{
    public const string MismatchReason = "type_mismatch";
    /// <summary>Bytes needed to recognise every accepted format.</summary>
    public const int HeadLength = 16;

    /// <summary>"image/jpeg", "image/png" or "image/webp" from the magic bytes, else null.</summary>
    public static string? Detect(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return "image/jpeg";
        if (head.Length >= 8 && head[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return "image/png";
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    /// <summary>Throws <see cref="InvalidImageException"/> unless <paramref name="declared"/> names the format of <paramref name="head"/>.</summary>
    public static void Verify(string? declared, ReadOnlySpan<byte> head)
    {
        var actual = Detect(head);
        var type = (declared ?? "").Split(';')[0].Trim();
        if (actual is null || !string.Equals(type, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidImageException(MismatchReason);
    }
}

/// <summary>The HTTP headers a stored blob is served with (S2-4).</summary>
public sealed record BlobHeaders(string ContentType, string ContentDisposition)
{
    private static readonly string[] PublicTypes = ["image/webp", "image/jpeg", "image/png"];

    /// <summary>
    /// Public variants and avatars are images shown inline, and only image types are accepted for them; everything private
    /// (originals, sealed copies, exports, evidence) is an attachment, so a browser that is handed a read URL downloads it.
    /// <c>X-Content-Type-Options: nosniff</c> cannot be stored on a blob; the CDN adds it (docs/deploy.md).
    /// </summary>
    public static BlobHeaders For(string name, string contentType)
    {
        var container = name.Split('/', 2)[0];
        if (container is BlobNames.Works or BlobNames.Avatars)
        {
            if (!PublicTypes.Contains(contentType)) throw new ArgumentException("Public blobs must be images.", nameof(contentType));
            return new BlobHeaders(contentType, "inline");
        }
        return new BlobHeaders(contentType, "attachment");
    }
}
