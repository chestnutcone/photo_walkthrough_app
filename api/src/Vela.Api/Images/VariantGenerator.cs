using SkiaSharp;

namespace Vela.Api.Images;

/// <remarks>The gate is required and must be the process-wide singleton (review F9): a per-instance gate would not limit anything.</remarks>
public sealed class VariantGenerator(IBlobStore blobs, ImageDecodeGate gate, ILogger<VariantGenerator>? log = null) : IVariantGenerator
{
    public const long MaxSourceBytes = 25L * 1024 * 1024;
    /// <summary>Checked from the codec header, before any pixel memory is allocated.</summary>
    public const long MaxPixels = 60_000_000;
    /// <summary>
    /// Native memory one decode may use: the output bitmap (twice when an EXIF rotation needs a second one) plus, for a progressive
    /// or multi-scan JPEG, libjpeg's full-resolution coefficient buffer. JPEG and WebP are decoded at reduced size (about the
    /// largest variant), so this binds PNG (about 30 MP) and progressive JPEG (about 5000x5000 at 4:2:0, 3900x3900 at 4:4:4).
    /// Measured peak RSS of the whole process, two concurrent worst-case decodes (the default gate): 404 MB (review round 1, F1).
    /// </summary>
    public const long MaxDecodeBytes = 120L * 1024 * 1024;
    /// <summary>Long edge of the largest public variant; decoding never needs more than this.</summary>
    public const int LargestVariantEdge = 2400;
    private const int BytesPerPixel = 4;
    private const int WebpQuality = 80;
    private const int AvatarSize = 256;
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private static readonly (string Name, int LongEdge)[] Sizes = [("thumb", 400), ("feed", 1080), ("full", LargestVariantEdge)];

    private readonly ImageDecodeGate _gate = gate ?? throw new ArgumentNullException(nameof(gate));

    public async Task<ImageVariants> GenerateAsync(string sourceBlob, Guid workId)
    {
        using var slot = await _gate.EnterAsync();
        var dir = $"{BlobNames.Works}/{workId}";
        var decoded = Decode(await ReadSourceAsync(sourceBlob), _ => LargestVariantEdge);
        // Largest first, each smaller size from the previous one: the full-resolution decode is resampled once, so no
        // mipmap chain or extra copy of it is ever built (measured in review round 1, F1).
        var current = decoded.Bitmap;
        var owned = new List<SKBitmap> { current };
        try
        {
            foreach (var (name, edge) in Sizes.OrderByDescending(x => x.LongEdge))
            {
                if (ScaleToFit(current, edge) is { } scaled) { owned.Add(scaled); current = scaled; }
                await WriteWebpAsync($"{dir}/{name}.webp", current);
                if (owned[0] != current && !owned[0].IsNull) owned[0].Reset(); // the full decode is no longer needed
            }
        }
        finally { owned.ForEach(b => b.Dispose()); }
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
            oriented.SetImmutable(); // so SKImage shares the pixels instead of copying the full decode
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
    /// <paramref name="targetLongEdge"/> where the codec can do so while decoding, and within <see cref="MaxDecodeBytes"/>
    /// counting the output bitmap(s) and, for a buffered JPEG (<paramref name="data"/> given), the coefficient image.
    /// </summary>
    public static DecodePlan PlanDecode(SKCodec codec, int targetLongEdge, byte[]? data)
    {
        var (w, h) = (codec.Info.Width, codec.Info.Height);
        if (w <= 0 || h <= 0) throw new InvalidImageException("corrupt");
        if ((long)w * h > MaxPixels) throw new InvalidImageException("too_many_pixels");
        var size = LongEdgeOf(w, h) > targetLongEdge ? SmallestScaledAtLeast(codec, targetLongEdge) : new SKSizeI(w, h);
        var copies = IsRotated(codec.EncodedOrigin) ? 2 : 1;
        var coefficients = codec.EncodedFormat == SKEncodedImageFormat.Jpeg && data is not null ? JpegCoefficientBytes(data) : 0;
        if ((long)size.Width * size.Height * BytesPerPixel * copies + coefficients > MaxDecodeBytes)
            throw new InvalidImageException("too_many_pixels");
        return new DecodePlan(size.Width, size.Height);
    }

    /// <summary>
    /// Native memory libjpeg needs for the whole-image DCT coefficient buffer (review F1). A progressive JPEG (SOF2/6/10/14), or a
    /// sequential one split over several scans, is decoded through this buffer at full resolution whatever the output size:
    /// for each component, blocks of 8x8 (rounded up to its sampling factors) times 64 coefficients times 2 bytes. A single-scan
    /// baseline JPEG streams and needs none (0). Not a JPEG, or no frame header: 0 (the codec refuses it later).
    /// </summary>
    public static long JpegCoefficientBytes(byte[] data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) return 0;
        for (var i = 2; i + 3 < data.Length;)
        {
            if (data[i] != 0xFF) { i++; continue; }
            var marker = data[i + 1];
            if (marker is 0xFF or 0x01 || marker is >= 0xD0 and <= 0xD7) { i += marker == 0xFF ? 1 : 2; continue; }
            if (marker is 0xD9 or 0xDA) return 0; // end of image or a scan before any frame header
            var length = (data[i + 2] << 8) | data[i + 3];
            if (IsFrameHeader(marker) && i + 10 <= data.Length) return CoefficientBytes(data, i, marker);
            i += 2 + length;
        }
        return 0;
    }

    private static bool IsFrameHeader(byte marker) => marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC);

    private static long CoefficientBytes(byte[] d, int sof, byte marker)
    {
        var progressive = marker is 0xC2 or 0xC6 or 0xCA or 0xCE;
        if (!progressive && d.AsSpan(sof).IndexOf(new byte[] { 0xFF, 0xDA }) is var first and >= 0
            && d.AsSpan(sof + first + 2).IndexOf(new byte[] { 0xFF, 0xDA }) < 0) return 0; // one sequential scan: streamed
        int h = (d[sof + 5] << 8) | d[sof + 6], w = (d[sof + 7] << 8) | d[sof + 8], n = d[sof + 9];
        if (sof + 10 + 3 * n > d.Length || n == 0) return 0;
        var comps = Enumerable.Range(0, n).Select(c => (H: Math.Max(1, d[sof + 11 + 3 * c] >> 4), V: Math.Max(1, d[sof + 11 + 3 * c] & 15))).ToList();
        int hMax = comps.Max(c => c.H), vMax = comps.Max(c => c.V);
        long total = 0;
        foreach (var (hs, vs) in comps)
        {
            long blocksW = CeilDiv(CeilDiv((long)w * hs, hMax), 8), blocksH = CeilDiv(CeilDiv((long)h * vs, vMax), 8);
            total += RoundUp(blocksW, hs) * RoundUp(blocksH, vs) * 64 * 2;
        }
        return total;
    }

    private static long CeilDiv(long a, long b) => (a + b - 1) / b;
    private static long RoundUp(long a, long b) => CeilDiv(a, b) * b;

    private static int LongEdgeOf(int w, int h) => Math.Max(w, h);

    /// <summary>
    /// The smallest decode size the codec supports whose long edge is still at least <paramref name="targetLongEdge"/>.
    /// <c>GetScaledDimensions</c> rounds down to a supported scale (JPEG: k/8; WebP: any; PNG: full size only), so each JPEG step and
    /// the exact ratio are tried and the smallest that is big enough wins; full size is the fallback.
    /// </summary>
    private static SKSizeI SmallestScaledAtLeast(SKCodec codec, int targetLongEdge)
    {
        var full = new SKSizeI(codec.Info.Width, codec.Info.Height);
        var exact = (float)targetLongEdge / LongEdgeOf(full.Width, full.Height);
        var best = full;
        foreach (var scale in Enumerable.Range(1, 7).Select(k => k / 8f).Append(exact).Append(exact * 1.001f))
        {
            if (scale >= 1f) continue;
            var d = codec.GetScaledDimensions(scale);
            if (d.Width <= 0 || d.Height <= 0 || LongEdgeOf(d.Width, d.Height) < targetLongEdge) continue;
            if ((long)d.Width * d.Height < (long)best.Width * best.Height) best = d;
        }
        return best;
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
        var plan = PlanDecode(codec, targetLongEdge(codec.Info), data);

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
        bitmap.SetImmutable(); // share, do not copy, the pixels
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, WebpQuality) ?? throw new InvalidImageException("corrupt");
        using var stream = data.AsStream();
        await blobs.WriteAsync(blob, stream, "image/webp");
    }
}
