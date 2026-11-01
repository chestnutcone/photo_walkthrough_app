using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Images;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Images;

/// <summary>S2-4: the bytes decide the type. An upload whose content does not match the type it declared is refused.</summary>
public class ContentSniffTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1];
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];
    private static readonly byte[] Webp = [.. "RIFF"u8, 0x24, 0, 0, 0, .. "WEBPVP8 "u8];

    [Fact]
    public void Detects_the_three_accepted_formats_from_their_magic_bytes()
    {
        Assert.Equal("image/jpeg", ImageSniffer.Detect(Jpeg));
        Assert.Equal("image/png", ImageSniffer.Detect(Png));
        Assert.Equal("image/webp", ImageSniffer.Detect(Webp));
    }

    [Theory]
    [InlineData("<html><script>alert(1)</script>")]
    [InlineData("GIF89a......")]
    [InlineData("RIFF\u0000\u0000\u0000\u0000WAVEfmt ")]
    [InlineData("%PDF-1.7")]
    [InlineData("")]
    public void Anything_else_is_unknown(string head) => Assert.Null(ImageSniffer.Detect(Encoding.Latin1.GetBytes(head)));

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("IMAGE/JPEG")]
    [InlineData("image/jpeg; charset=binary")]
    public void Matching_declaration_passes(string declared) => ImageSniffer.Verify(declared, Jpeg);

    [Theory]
    [InlineData("image/png")]
    [InlineData("text/html")]
    [InlineData("application/octet-stream")]
    [InlineData(null)]
    [InlineData("")]
    public void Mismatched_or_missing_declaration_is_refused(string? declared)
    {
        var e = Assert.Throws<InvalidImageException>(() => ImageSniffer.Verify(declared, Jpeg));
        Assert.Equal(ImageSniffer.MismatchReason, e.Reason);
    }

    [Fact]
    public void Html_declared_as_jpeg_is_refused() =>
        Assert.Throws<InvalidImageException>(() => ImageSniffer.Verify("image/jpeg", "<html>"u8.ToArray()));

    [Theory]
    [InlineData("works/w1/feed.webp", "image/webp", "inline")]
    [InlineData("avatars/u1/256.webp", "image/webp", "inline")]
    [InlineData("originals/sealed/w1/abc", "application/octet-stream", "attachment")]
    [InlineData("exports/u1/export.zip", "application/zip", "attachment")]
    [InlineData("evidence/h1/original", "application/octet-stream", "attachment")]
    public void Stored_blobs_get_a_safe_disposition(string name, string type, string disposition)
    {
        var h = BlobHeaders.For(name, type);
        Assert.Equal(type, h.ContentType);
        Assert.Equal(disposition, h.ContentDisposition);
    }

    [Fact]
    public void Public_blobs_cannot_be_stored_with_an_active_type()
    {
        Assert.Throws<ArgumentException>(() => BlobHeaders.For("works/w1/x.html", "text/html"));
        Assert.Throws<ArgumentException>(() => BlobHeaders.For("avatars/u1/x.svg", "image/svg+xml"));
    }
}
