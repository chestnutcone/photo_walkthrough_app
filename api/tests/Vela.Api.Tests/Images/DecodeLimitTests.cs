using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using SkiaSharp;
using Vela.Api.Images;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Images;

/// <summary>S1b-1: decoding is bounded by a header check, a decode-memory budget, reduced-size decoding and a process-wide gate.</summary>
public class DecodeLimitTests
{
    private readonly InMemoryBlobStore _blobs = new();

    /// <summary>A valid PNG signature and IHDR that claims <paramref name="w"/> x <paramref name="h"/>, then only the first bytes of the pixel stream.</summary>
    public static byte[] PngHeaderOnly(int w, int h)
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
        var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(ms, "IHDR", ihdr);
        Chunk(ms, "IDAT", [0x78, 0x9C, 0x63, 0x60, 0x00, 0x00]); // a zlib stream that stops after one row's worth of nothing
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        s.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(len, Crc32(typed));
        s.Write(len);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    [Fact]
    public async Task A_png_header_claiming_20000x20000_is_rejected_from_the_header_alone()
    {
        var work = Guid.NewGuid();
        _blobs.Put(BlobNames.WorkOriginal(work), PngHeaderOnly(20000, 20000)); // 400 MP, 1.6 GB if decoded
        var gen = new VariantGenerator(_blobs);

        var watch = Stopwatch.StartNew();
        var e = await Assert.ThrowsAsync<InvalidImageException>(() => gen.GenerateAsync(BlobNames.WorkOriginal(work), work));

        Assert.Equal("too_many_pixels", e.Reason);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
        Assert.DoesNotContain(_blobs.Names, n => n.StartsWith("works/", StringComparison.Ordinal));
    }

    [Fact]
    public void A_large_jpeg_is_decoded_at_reduced_size()
    {
        using var codec = SKCodec.Create(new MemoryStream(ImageFixtures.Jpeg(4800, 4800)))!;

        var plan = VariantGenerator.PlanDecode(codec, VariantGenerator.LargestVariantEdge);

        Assert.True(plan.Width < 4800 && plan.Width >= VariantGenerator.LargestVariantEdge, $"decoded at {plan.Width}x{plan.Height}");
        Assert.Equal(plan.Width, plan.Height);
    }

    [Fact]
    public void A_png_that_cannot_be_decoded_at_reduced_size_and_exceeds_the_decode_budget_is_rejected()
    {
        // 48 MP is under the 60 MP pixel cap, but Skia decodes PNG only at full size: 192 MB of pixels is over the budget.
        using var codec = SKCodec.Create(new MemoryStream(ImageFixtures.BombPng(8000, 6000)))!;

        var e = Assert.Throws<InvalidImageException>(() => VariantGenerator.PlanDecode(codec, VariantGenerator.LargestVariantEdge));

        Assert.Equal("too_many_pixels", e.Reason);
    }

    [Fact]
    public async Task Large_sources_still_produce_exact_variant_sizes_and_the_original_dimensions()
    {
        var work = Guid.NewGuid();
        _blobs.Put(BlobNames.WorkOriginal(work), ImageFixtures.Jpeg(4800, 2400));

        var v = await new VariantGenerator(_blobs).GenerateAsync(BlobNames.WorkOriginal(work), work);

        Assert.Equal((4800, 2400), (v.Width, v.Height));
        using var full = SKCodec.Create(new MemoryStream(_blobs.Bytes($"works/{work}/full.webp")))!;
        Assert.Equal((2400, 1200), (full.Info.Width, full.Info.Height));
    }

    [Fact]
    public async Task Concurrent_decodes_never_exceed_the_gate_limit()
    {
        using var gate = new ImageDecodeGate(2, TimeSpan.FromSeconds(30));
        var users = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToList();
        foreach (var u in users) _blobs.Put(BlobNames.AvatarOriginal(u), ImageFixtures.Png(300, 300));
        _blobs.BeforeRead = _ => Task.Delay(50); // every decode stays in flight long enough to overlap
        var gen = new VariantGenerator(_blobs, gate);

        await Task.WhenAll(users.Select(u => Task.Run(() => gen.GenerateAvatarAsync(BlobNames.AvatarOriginal(u), u))));

        Assert.Equal(2, gate.Peak);
        Assert.All(users, u => Assert.True(_blobs.Exists($"avatars/{u}/avatar-256.webp")));
    }

    [Fact]
    public async Task A_decode_that_cannot_get_a_slot_in_time_is_busy_and_keeps_the_source()
    {
        using var gate = new ImageDecodeGate(1, TimeSpan.FromMilliseconds(50));
        var user = Guid.NewGuid();
        _blobs.Put(BlobNames.AvatarOriginal(user), ImageFixtures.Png(100, 100));
        var gen = new VariantGenerator(_blobs, gate);

        using (await gate.EnterAsync())
        {
            var e = await Assert.ThrowsAsync<ImageBusyException>(() => gen.GenerateAvatarAsync(BlobNames.AvatarOriginal(user), user));
            Assert.True(e.RetryAfter > TimeSpan.Zero);
        }

        Assert.True(_blobs.Exists(BlobNames.AvatarOriginal(user)));
        await gen.GenerateAvatarAsync(BlobNames.AvatarOriginal(user), user); // the slot is free again
    }

    [Fact]
    public async Task A_failed_avatar_decode_removes_the_upload_so_it_cannot_be_replayed()
    {
        var user = Guid.NewGuid();
        _blobs.Put(BlobNames.AvatarOriginal(user), ImageFixtures.Jpeg(800, 800).Take(300).ToArray());

        await Assert.ThrowsAsync<InvalidImageException>(() => new VariantGenerator(_blobs).GenerateAvatarAsync(BlobNames.AvatarOriginal(user), user));

        Assert.False(_blobs.Exists(BlobNames.AvatarOriginal(user)));
    }
}
