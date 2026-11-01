using SkiaSharp;

namespace Vela.Api.Tests.Support;

/// <summary>Builds small test images in memory; nothing is read from disk.</summary>
public static class ImageFixtures
{
    public static SKBitmap Halves(int w, int h, SKColor left, SKColor right)
    {
        var bmp = new SKBitmap(w, h);
        using var c = new SKCanvas(bmp);
        using var p = new SKPaint { Color = left };
        c.DrawRect(0, 0, w / 2f, h, p);
        p.Color = right;
        c.DrawRect(w / 2f, 0, w - w / 2f, h, p);
        return bmp;
    }

    public static byte[] Encode(SKBitmap bmp, SKEncodedImageFormat format, int quality = 90)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(format, quality);
        return data.ToArray();
    }

    public static byte[] Jpeg(int w, int h) => Encode(Halves(w, h, SKColors.Red, SKColors.Blue), SKEncodedImageFormat.Jpeg);
    public static byte[] Png(int w, int h) => Encode(Halves(w, h, SKColors.Red, SKColors.Blue), SKEncodedImageFormat.Png);
    public static byte[] WebP(int w, int h) => Encode(Halves(w, h, SKColors.Red, SKColors.Blue), SKEncodedImageFormat.Webp);

    /// <summary>A flat PNG with a huge pixel count that compresses to a few KB (decompression-bomb shape).</summary>
    public static byte[] BombPng(int w, int h)
    {
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Gray8, SKAlphaType.Opaque));
        return Encode(bmp, SKEncodedImageFormat.Png);
    }

    /// <summary>
    /// JPEG whose left half is red and right half blue, stored landscape, tagged EXIF orientation 6
    /// (rotate 90 CW) and carrying a GPS IFD with latitude data.
    /// </summary>
    public static byte[] JpegWithExifOrientation6AndGps(int storedW, int storedH)
    {
        var plain = Jpeg(storedW, storedH);
        var exif = BuildExif(orientation: 6);
        var app1 = new List<byte> { 0xFF, 0xE1, (byte)((exif.Length + 2) >> 8), (byte)((exif.Length + 2) & 0xFF) };
        app1.AddRange(exif);
        return [.. plain.Take(2), .. app1, .. plain.Skip(2)];
    }

    private static byte[] BuildExif(ushort orientation)
    {
        var b = new List<byte>();
        void U16(int v) { b.Add((byte)(v & 0xFF)); b.Add((byte)(v >> 8)); }
        void U32(int v) { U16(v & 0xFFFF); U16(v >> 16); }
        b.AddRange("Exif\0\0"u8.ToArray());
        b.AddRange("II"u8.ToArray()); U16(42); U32(8);          // TIFF header, IFD0 at 8
        U16(2);                                                  // IFD0: 2 entries
        U16(0x0112); U16(3); U32(1); U16(orientation); U16(0);   // Orientation
        U16(0x8825); U16(4); U32(1); U32(38);                    // GPSInfo -> IFD at 38
        U32(0);                                                  // no next IFD
        U16(2);                                                  // GPS IFD: 2 entries
        U16(1); U16(2); U32(2); b.AddRange("N\0\0\0"u8.ToArray()); // GPSLatitudeRef
        U16(2); U16(5); U32(3); U32(68);                         // GPSLatitude -> data at 68
        U32(0);
        foreach (var (n, d) in new[] { (40, 1), (26, 1), (46, 1) }) { U32(n); U32(d); }
        return b.ToArray();
    }
}
