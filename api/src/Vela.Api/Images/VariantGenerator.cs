using SkiaSharp;

namespace Vela.Api.Images;

public sealed class VariantGenerator(IBlobStore blobs, ImageDecodeGate? gate = null, ILogger<VariantGenerator>? log = null) : IVariantGenerator
{
    public const long MaxSourceBytes = 25L * 1024 * 1024;
    /// <summary>Checked from the codec header, before any pixel memory is allocated.</summary>
    public const long MaxPixels = 60_000_000;
    /// <summary>
    /// Native pixel memory one decode may use, counting the second bitmap an EXIF rotation needs. JPEG and WebP are decoded at
    /// reduced size (about the largest variant), so this binds only formats Skia cannot downscale while decoding (PNG):
    /// 160 MB is a 40 MP PNG, or 20 MP when it carries a rotation.
    /// </summary>
    public const long MaxDecodeBytes = 160L * 1024 * 1024;
    /// <summary>Long edge of the largest public variant; decoding never needs more than this.</summary>
    public const int LargestVariantEdge = 2400;
    private const int BytesPerPixel = 4;
    private const int WebpQuality = 80;
    private const int AvatarSize = 256;
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private static readonly (string Name, int LongEdge)[] Sizes = [("thumb", 400), ("feed", 1080), ("full", LargestVariantEdge)];

    private readonly ImageDecodeGate _gate = gate ?? new ImageDecodeGate(ImageDecodeGate.DefaultMaxConcurrentDecodes, ImageDecodeGate.DefaultWait);

    public async Task<ImageVariants> GenerateAsync(string sourceBlob, Guid workId)
    {
        using var slot = await _gate.EnterAsync();
        var dir = $"{BlobNames.Works}/{workId}";
        var decoded = Decode(await ReadSourceAsync(sourceBlob), _ => LargestVariantEdge);
        using var oriented = decoded.Bitmap;
        foreach (var (name, edge) in Sizes)
        {
            using var scaled = ScaleToFit(oriented, edge);
            await WriteWebpAsync($"{dir}/{name}.webp", scaled ?? oriented);
        }
        return new ImageVariants(decoded.Width, decoded.Height, $"{dir}/thumb.webp", $"{dir}/feed.webp", $"{dir}/full.webp");
    }

    public async Task<string> GenerateAvatarAsync(string sourceBlob, Guid userId)
    {
        string name;
        using (await _gate.EnterAsync())
        {
            Decoded decoded;
            try
            {
                // Decode just large enough that the short edge still covers the 256 px square.
                decoded = Decode(await ReadSourceAsync(sourceBlob),
                    info => (int)Math.Ceiling((double)AvatarSize * Math.Max(info.Width, info.Height) / Math.Max(1, Math.Min(info.Width, info.Height))));
            }
            catch (InvalidImageException)
            {
                // A failing upload must not be replayable for free (S1b-1): it is removed, and the user uploads again.
                await DeleteSourceAsync(sourceBlob);
                throw;
            }
            using var oriented = decoded.Bitmap;
            var side = Math.Min(oriented.Width, oriented.Height);
            var crop = SKRect.Create((oriented.Width - side) / 2f, (oriented.Height - side) / 2f, side, side);
            using var square = new SKBitmap(AvatarSize, AvatarSize);
            using (var canvas = new SKCanvas(square))
            using (var image = SKImage.FromBitmap(oriented))
                canvas.DrawImage(image, crop, SKRect.Create(AvatarSize, AvatarSize), Sampling, null);
            name = $"avatars/{userId}/avatar-256.webp";
            await WriteWebpAsync(name, square);
        }
        // The raw upload is not needed once the avatar exists. A failed delete must not turn a finished avatar into an error:
        // the stale original lives in the private originals container and is overwritten by the next upload.
        await DeleteSourceAsync(sourceBlob);
        return name;
    }

    private async Task DeleteSourceAsync(string sourceBlob)
    {
        try { await blobs.DeletePrefixAsync(sourceBlob); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log?.LogWarning("Could not delete a raw avatar upload ({Reason}).", e.GetType().Name);
        }
    }

    private async Task<byte[]> ReadSourceAsync(string blob)
    {
        try
        {
            await using var stream = await blobs.OpenReadAsync(blob);
            if (stream.CanSeek && stream.Length > MaxSourceBytes) throw new InvalidImageException("too_large");
            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                if (ms.Length + read > MaxSourceBytes) throw new InvalidImageException("too_large");
                ms.Write(buffer, 0, read);
            }
            return ms.ToArray();
        }
        catch (FileNotFoundException)
        {
            throw new InvalidImageException("missing");
        }
    }

    /// <summary>Decoded pixels (already oriented, possibly reduced) and the oriented size of the original.</summary>
    private sealed record Decoded(SKBitmap Bitmap, int Width, int Height);

    /// <summary>Decoded size before orientation is applied.</summary>
    public sealed record DecodePlan(int Width, int Height);

    /// <summary>
    /// Chooses the decode size from the codec header only: at most <see cref="MaxPixels"/>, scaled down towards
    /// <paramref name="targetLongEdge"/> where the codec can do so while decoding, and within <see cref="MaxDecodeBytes"/>.
    /// </summary>
    public static DecodePlan PlanDecode(SKCodec codec, int targetLongEdge)
    {
        var (w, h) = (codec.Info.Width, codec.Info.Height);
        if (w <= 0 || h <= 0) throw new InvalidImageException("corrupt");
        if ((long)w * h > MaxPixels) throw new InvalidImageException("too_many_pixels");
        var longEdge = Math.Max(w, h);
        var size = new SKSizeI(w, h);
        if (longEdge > targetLongEdge)
        {
            // A little over the target so rounding never lands below it; JPEG rounds its 1/8 steps up, WebP scales exactly.
            var scaled = codec.GetScaledDimensions((float)Math.Min(1.0, (targetLongEdge + 2.0) / longEdge));
            if (scaled.Width > 0 && scaled.Height > 0 && Math.Max(scaled.Width, scaled.Height) >= targetLongEdge) size = scaled;
        }
        var copies = IsRotated(codec.EncodedOrigin) ? 2 : 1;
        if ((long)size.Width * size.Height * BytesPerPixel * copies > MaxDecodeBytes) throw new InvalidImageException("too_many_pixels");
        return new DecodePlan(size.Width, size.Height);
    }

    private static bool IsRotated(SKEncodedOrigin origin) => origin is not (SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default);

    private static bool SwapsAxes(SKEncodedOrigin origin) =>
        origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    /// <summary>Sniffs the content, checks the header, then decodes (reduced where possible) and applies EXIF orientation.</summary>
    private static Decoded Decode(byte[] data, Func<SKImageInfo, int> targetLongEdge)
    {
        var format = Sniff(data) ?? throw new InvalidImageException("unsupported_format");
        using var codec = SKCodec.Create(new MemoryStream(data)) ?? throw new InvalidImageException("corrupt");
        if (codec.EncodedFormat != format) throw new InvalidImageException("unsupported_format");
        var plan = PlanDecode(codec, targetLongEdge(codec.Info));

        var bitmap = new SKBitmap(new SKImageInfo(plan.Width, plan.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (result is not SKCodecResult.Success)
        {
            bitmap.Dispose();
            throw new InvalidImageException("corrupt");
        }
        var swap = SwapsAxes(codec.EncodedOrigin);
        var (w, h) = (codec.Info.Width, codec.Info.Height);
        return new Decoded(ImageOrientation.Apply(bitmap, codec.EncodedOrigin), swap ? h : w, swap ? w : h);
    }

    private static SKEncodedImageFormat? Sniff(byte[] d)
    {
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return SKEncodedImageFormat.Jpeg;
        if (d.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return SKEncodedImageFormat.Png;
        if (d.Length >= 12 && d.AsSpan(0, 4).SequenceEqual("RIFF"u8) && d.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return SKEncodedImageFormat.Webp;
        return null;
    }

    /// <summary>Null when the image already fits (never upscale).</summary>
    private static SKBitmap? ScaleToFit(SKBitmap src, int longEdge)
    {
        var longest = Math.Max(src.Width, src.Height);
        if (longest <= longEdge) return null;
        var scale = (double)longEdge / longest;
        var info = new SKImageInfo(Math.Max(1, (int)Math.Round(src.Width * scale)), Math.Max(1, (int)Math.Round(src.Height * scale)),
            SKColorType.Rgba8888, SKAlphaType.Premul);
        return src.Resize(info, Sampling) ?? throw new InvalidImageException("corrupt");
    }

    /// <summary>Skia's encoder writes pixels only, so no EXIF, GPS, XMP or ICC data reaches the output.</summary>
    private async Task WriteWebpAsync(string blob, SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, WebpQuality) ?? throw new InvalidImageException("corrupt");
        using var stream = data.AsStream();
        await blobs.WriteAsync(blob, stream, "image/webp");
    }
}
