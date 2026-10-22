using SkiaSharp;

namespace Vela.Api.Images;

public sealed class VariantGenerator(IBlobStore blobs) : IVariantGenerator
{
    public const long MaxSourceBytes = 25L * 1024 * 1024;
    public const long MaxPixels = 60_000_000;
    private const int WebpQuality = 80;
    private const int AvatarSize = 256;
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private static readonly (string Name, int LongEdge)[] Sizes = [("thumb", 400), ("feed", 1080), ("full", 2400)];

    public async Task<ImageVariants> GenerateAsync(string sourceBlob)
    {
        var dir = sourceBlob[..sourceBlob.LastIndexOf('/')];
        using var oriented = Decode(await ReadSourceAsync(sourceBlob));
        var names = new List<string>();
        foreach (var (name, edge) in Sizes)
        {
            var blob = $"{dir}/{name}.webp";
            using var scaled = ScaleToFit(oriented, edge);
            await WriteWebpAsync(blob, scaled ?? oriented);
            names.Add(blob);
        }
        return new ImageVariants(oriented.Width, oriented.Height, names[0], names[1], names[2]);
    }

    public async Task<string> GenerateAvatarAsync(string sourceBlob, Guid userId)
    {
        using var oriented = Decode(await ReadSourceAsync(sourceBlob));
        var side = Math.Min(oriented.Width, oriented.Height);
        var crop = SKRect.Create((oriented.Width - side) / 2f, (oriented.Height - side) / 2f, side, side);
        using var square = new SKBitmap(AvatarSize, AvatarSize);
        using (var canvas = new SKCanvas(square))
        using (var image = SKImage.FromBitmap(oriented))
            canvas.DrawImage(image, crop, SKRect.Create(AvatarSize, AvatarSize), Sampling, null);
        var name = $"avatars/{userId}/avatar-256.webp";
        await WriteWebpAsync(name, square);
        return name;
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

    /// <summary>Sniffs the content, checks the declared size, then decodes and applies EXIF orientation.</summary>
    private static SKBitmap Decode(byte[] data)
    {
        var format = Sniff(data) ?? throw new InvalidImageException("unsupported_format");
        using var codec = SKCodec.Create(new MemoryStream(data)) ?? throw new InvalidImageException("corrupt");
        if (codec.EncodedFormat != format) throw new InvalidImageException("unsupported_format");
        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels) throw new InvalidImageException("too_many_pixels");
        if (codec.Info.Width <= 0 || codec.Info.Height <= 0) throw new InvalidImageException("corrupt");

        var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success))
        {
            bitmap.Dispose();
            throw new InvalidImageException("corrupt");
        }
        return ImageOrientation.Apply(bitmap, codec.EncodedOrigin);
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
