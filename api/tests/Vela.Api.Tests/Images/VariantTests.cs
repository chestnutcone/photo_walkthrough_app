using SkiaSharp;
using Vela.Api.Images;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Images;

public class VariantTests
{
    private readonly InMemoryBlobStore _blobs = new();
    private readonly VariantGenerator _gen;
    private static readonly Guid NoneId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid W1Id = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid W2Id = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid W3Id = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid W4Id = Guid.Parse("00000000-0000-0000-0000-000000000005");
    private static readonly Guid BadId = Guid.Parse("00000000-0000-0000-0000-000000000006");

    public VariantTests() => _gen = new VariantGenerator(_blobs);

    private static (int W, int H) Size(byte[] data)
    {
        using var codec = SKCodec.Create(new MemoryStream(data))!;
        return (codec.Info.Width, codec.Info.Height);
    }

    private static SKBitmap Decode(byte[] data) => SKBitmap.Decode(data);

    private static bool IsWebP(byte[] d) => d.Length > 12 && d[0] == 'R' && d[1] == 'I' && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P';

    [Theory]
    [InlineData("jpeg")]
    [InlineData("png")]
    [InlineData("webp")]
    public async Task Variants_are_generated_in_three_sizes_as_webp(string format)
    {
        byte[] src = format switch { "jpeg" => ImageFixtures.Jpeg(3000, 1500), "png" => ImageFixtures.Png(3000, 1500), _ => ImageFixtures.WebP(3000, 1500) };
        _blobs.Put("originals/works/w1", src);

        var v = await _gen.GenerateAsync("originals/works/w1", W1Id);

        Assert.Equal((3000, 1500), (v.Width, v.Height));
        Assert.Equal((400, 200), Size(_blobs.Bytes($"works/{W1Id}/thumb.webp")));
        Assert.Equal((1080, 540), Size(_blobs.Bytes($"works/{W1Id}/feed.webp")));
        Assert.Equal((2400, 1200), Size(_blobs.Bytes($"works/{W1Id}/full.webp")));
        Assert.All(new[] { "thumb", "feed", "full" }, n => Assert.True(IsWebP(_blobs.Bytes($"works/{W1Id}/{n}.webp"))));
        Assert.True(_blobs.Exists("originals/works/w1"));
    }

    [Fact]
    public async Task Small_images_are_never_upscaled()
    {
        _blobs.Put("originals/works/w2", ImageFixtures.Png(300, 200));

        var v = await _gen.GenerateAsync("originals/works/w2", W2Id);

        Assert.Equal((300, 200), (v.Width, v.Height));
        Assert.Equal((300, 200), Size(_blobs.Bytes($"works/{W2Id}/thumb.webp")));
        Assert.Equal((300, 200), Size(_blobs.Bytes($"works/{W2Id}/full.webp")));
    }

    [Fact]
    public void Fixture_really_carries_orientation_6_and_gps()
    {
        var src = ImageFixtures.JpegWithExifOrientation6AndGps(400, 200);
        using var codec = SKCodec.Create(new MemoryStream(src))!;
        Assert.Equal(SKEncodedOrigin.RightTop, codec.EncodedOrigin);
        Assert.True(src.AsSpan().IndexOf(new byte[] { 0x25, 0x88, 0x04, 0x00 }) > 0, "GPSInfo tag present in fixture");
        Assert.True(src.AsSpan().IndexOf("Exif"u8) > 0);
    }

    [Fact]
    public async Task Exif_rotated_portrait_photo_reports_corrected_width_and_height()
    {
        // Stored 400x200 (landscape): left half red, right half blue, orientation 6 = rotate 90 CW.
        _blobs.Put("originals/works/w3", ImageFixtures.JpegWithExifOrientation6AndGps(400, 200));

        var v = await _gen.GenerateAsync("originals/works/w3", W3Id);

        Assert.Equal((200, 400), (v.Width, v.Height));
        foreach (var n in new[] { "thumb", "feed", "full" })
        {
            using var bmp = Decode(_blobs.Bytes($"works/{W3Id}/{n}.webp"));
            Assert.Equal((200, 400), (bmp.Width, bmp.Height));
            var top = bmp.GetPixel(bmp.Width / 2, bmp.Height / 4);
            var bottom = bmp.GetPixel(bmp.Width / 2, bmp.Height * 3 / 4);
            Assert.True(top.Red > 180 && top.Blue < 80, $"{n} top should be red, was {top}");
            Assert.True(bottom.Blue > 180 && bottom.Red < 80, $"{n} bottom should be blue, was {bottom}");
        }
    }

    [Fact]
    public async Task All_metadata_including_gps_is_stripped_from_every_output()
    {
        _blobs.Put("originals/works/w4", ImageFixtures.JpegWithExifOrientation6AndGps(400, 200));

        await _gen.GenerateAsync("originals/works/w4", W4Id);

        foreach (var n in new[] { "thumb", "feed", "full" })
        {
            var text = System.Text.Encoding.Latin1.GetString(_blobs.Bytes($"works/{W4Id}/{n}.webp"));
            Assert.DoesNotContain("EXIF", text);
            Assert.DoesNotContain("Exif", text);
            Assert.DoesNotContain("XMP", text);
            Assert.DoesNotContain("ICCP", text);
        }
    }

    [Fact]
    public async Task Non_image_bytes_are_rejected_whatever_the_name_says()
    {
        _blobs.Put("originals/works/x", "<html>not an image</html>"u8.ToArray());
        var e = await Assert.ThrowsAsync<InvalidImageException>(() => _gen.GenerateAsync("originals/works/x", BadId));
        Assert.Equal("unsupported_format", e.Reason);
    }

    [Fact]
    public async Task Gif_is_rejected_as_unsupported()
    {
        _blobs.Put("originals/works/x", "GIF89a\x01\0\x01\0\0\0\0;"u8.ToArray());
        var e = await Assert.ThrowsAsync<InvalidImageException>(() => _gen.GenerateAsync("originals/works/x", BadId));
        Assert.Equal("unsupported_format", e.Reason);
    }

    [Fact]
    public async Task Oversized_source_is_rejected_before_decoding()
    {
        var big = new byte[25 * 1024 * 1024 + 1];
        ImageFixtures.Jpeg(10, 10).CopyTo(big, 0);
        _blobs.Put("originals/works/x", big);
        var e = await Assert.ThrowsAsync<InvalidImageException>(() => _gen.GenerateAsync("originals/works/x", BadId));
        Assert.Equal("too_large", e.Reason);
    }

    [Fact]
    public async Task Decompression_bomb_over_60_megapixels_is_rejected()
    {
        _blobs.Put("originals/works/x", ImageFixtures.BombPng(8000, 8000)); // 64 MP, tiny file
        var e = await Assert.ThrowsAsync<InvalidImageException>(() => _gen.GenerateAsync("originals/works/x", BadId));
        Assert.Equal("too_many_pixels", e.Reason);
    }

    [Fact]
    public async Task Truncated_image_is_rejected_as_corrupt()
    {
        _blobs.Put("originals/works/x", ImageFixtures.Jpeg(800, 800).Take(300).ToArray());
        var e = await Assert.ThrowsAsync<InvalidImageException>(() => _gen.GenerateAsync("originals/works/x", BadId));
        Assert.Equal("corrupt", e.Reason);
    }

    [Fact]
    public async Task Missing_source_is_rejected()
    {
        var e = await Assert.ThrowsAsync<InvalidImageException>(() => _gen.GenerateAsync("originals/works/none", NoneId));
        Assert.Equal("missing", e.Reason);
    }

    [Fact]
    public async Task Avatar_is_a_center_cropped_256_square()
    {
        var user = Guid.NewGuid();
        _blobs.Put($"originals/avatars/{user}", ImageFixtures.Jpeg(400, 200)); // red | blue

        var name = await _gen.GenerateAvatarAsync($"originals/avatars/{user}", user);

        Assert.Equal($"avatars/{user}/avatar-256.webp", name);
        using var bmp = Decode(_blobs.Bytes(name));
        Assert.Equal((256, 256), (bmp.Width, bmp.Height));
        Assert.True(bmp.GetPixel(20, 128).Red > 180);
        Assert.True(bmp.GetPixel(236, 128).Blue > 180);
    }

    [Fact]
    public async Task Invalid_avatar_is_rejected()
    {
        var user = Guid.NewGuid();
        _blobs.Put($"originals/avatars/{user}", [1, 2, 3]);
        await Assert.ThrowsAsync<InvalidImageException>(() => _gen.GenerateAvatarAsync($"originals/avatars/{user}", user));
    }

    [Fact]
    public async Task Variants_never_land_in_originals_and_originals_never_in_public_containers()
    {
        var workId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _blobs.Put(BlobNames.WorkOriginal(workId), ImageFixtures.Jpeg(500, 500));
        _blobs.Put(BlobNames.AvatarOriginal(userId), ImageFixtures.Jpeg(500, 500));

        await _gen.GenerateAsync(BlobNames.WorkOriginal(workId), workId);
        await _gen.GenerateAvatarAsync(BlobNames.AvatarOriginal(userId), userId);

        Assert.All(_blobs.Names.Where(n => n.StartsWith("originals/")), n => Assert.DoesNotContain(".webp", n));
        Assert.All(_blobs.Names.Where(n => !n.StartsWith("originals/")), n => Assert.EndsWith(".webp", n));
        Assert.Contains($"works/{workId}/thumb.webp", _blobs.Names);
    }

    [Fact]
    public void Public_url_for_an_original_throws()
    {
        Assert.Throws<ArgumentException>(() => _blobs.PublicUrl(BlobNames.WorkOriginal(Guid.NewGuid())));
    }

    [Fact]
    public async Task Avatar_original_is_deleted_after_success_and_after_failure()
    {
        var ok = Guid.NewGuid();
        var bad = Guid.NewGuid();
        _blobs.Put(BlobNames.AvatarOriginal(ok), ImageFixtures.Png(100, 100));
        _blobs.Put(BlobNames.AvatarOriginal(bad), [1, 2, 3]);

        await _gen.GenerateAvatarAsync(BlobNames.AvatarOriginal(ok), ok);
        await Assert.ThrowsAsync<InvalidImageException>(() => _gen.GenerateAvatarAsync(BlobNames.AvatarOriginal(bad), bad));

        Assert.False(_blobs.Exists(BlobNames.AvatarOriginal(ok)));
        Assert.True(_blobs.Exists($"avatars/{ok}/avatar-256.webp"));
        Assert.False(_blobs.Exists(BlobNames.AvatarOriginal(bad))); // S1b-1: a failing upload is not kept for replays
    }

    [Fact]
    public async Task Work_original_is_kept_after_generation()
    {
        var id = Guid.NewGuid();
        _blobs.Put(BlobNames.WorkOriginal(id), ImageFixtures.Jpeg(100, 100));
        await _gen.GenerateAsync(BlobNames.WorkOriginal(id), id);
        Assert.True(_blobs.Exists(BlobNames.WorkOriginal(id)));
    }
}
