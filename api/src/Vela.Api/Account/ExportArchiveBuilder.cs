using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vela.Api.Branding;
using Vela.Api.Data;
using Vela.Api.Images;

namespace Vela.Api.Account;

/// <summary>
/// Builds a user's export zip into a temp file (deleted on close) and uploads it, so images stream through one at a time and
/// the whole archive is never held in memory.
/// </summary>
public sealed class ExportArchiveBuilder(VelaDbContext db, ExportData data, IBlobStore blobs, IOptions<BrandingOptions> branding)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task BuildAsync(Guid userId, string blobName, CancellationToken ct)
    {
        var profile = await data.ProfileAsync(userId);
        var works = await data.WorksAsync(userId);
        var path = Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid():N}.tmp");
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        var files = new List<string>();
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            await AddJsonAsync(zip, files, "profile.json", profile, ct);
            await AddJsonAsync(zip, files, "account.json", await data.AccountAsync(userId), ct);
            await AddJsonAsync(zip, files, "works.json", works, ct);
            await AddJsonAsync(zip, files, "replies.json", await data.RepliesAsync(userId), ct);
            await AddJsonAsync(zip, files, "follows.json", await data.FollowsAsync(userId), ct);
            await AddJsonAsync(zip, files, "blocks.json", await data.BlocksAsync(userId), ct);
            await AddJsonAsync(zip, files, "notifications.json", await data.NotificationsAsync(userId), ct);
            await AddJsonAsync(zip, files, "inquiries_sent.json", await data.InquiriesSentAsync(userId), ct);
            await AddJsonAsync(zip, files, "inquiries_received_summary.json", await data.InquiriesReceivedAsync(userId), ct);
            await AddJsonAsync(zip, files, "reports_filed.json", await data.ReportsFiledAsync(userId), ct);
            await AddJsonAsync(zip, files, "dmca_notices_about_my_works.json", await data.DmcaNoticesAboutMyWorksAsync(userId), ct);
            foreach (var work in works)
                if (await AddImageAsync(zip, BlobNames.WorkOriginal(work.Id), $"images/works/{work.Id}/original", ct) is { } name) files.Add(name);
            var avatarBlob = await db.Profiles.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.AvatarBlob).SingleOrDefaultAsync(ct);
            if (avatarBlob is not null && await AddImageAsync(zip, avatarBlob, "images/avatar", ct, forcedExtension: "webp") is { } avatar) files.Add(avatar);
            await AddReadmeAsync(zip, files, ct);
        }
        file.Position = 0;
        await blobs.WriteAsync(blobName, file, "application/zip");
    }

    private static async Task AddJsonAsync<T>(ZipArchive zip, List<string> files, string name, T value, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, ct);
        files.Add(name);
    }

    /// <summary>Streams a blob into the zip; returns the entry name, or null when the blob does not exist.</summary>
    private async Task<string?> AddImageAsync(ZipArchive zip, string blob, string stem, CancellationToken ct, string? forcedExtension = null)
    {
        Stream source;
        try { source = await blobs.OpenReadAsync(blob); }
        catch (FileNotFoundException) { return null; }
        await using (source)
        {
            var head = new byte[16];
            var read = await source.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
            var name = $"{stem}.{forcedExtension ?? ImageSniffer.Extension(head.AsSpan(0, read))}";
            var entry = zip.CreateEntry(name, CompressionLevel.NoCompression); // images are already compressed
            await using var target = entry.Open();
            await target.WriteAsync(head.AsMemory(0, read), ct);
            await source.CopyToAsync(target, ct);
            return name;
        }
    }

    private async Task AddReadmeAsync(ZipArchive zip, List<string> files, CancellationToken ct)
    {
        var text = new StringBuilder($"{branding.Value.AppName} data export\n\nThis archive holds the data {branding.Value.AppName} keeps about your account.\n")
            .Append("All JSON files are UTF-8 and every timestamp is ISO-8601.\n\nFiles:\n");
        foreach (var f in files.Append("README.txt")) text.Append("  ").Append(f).Append('\n');
        text.Append("\nOther people's emails, messages and details are not included.\n");
        var entry = zip.CreateEntry("README.txt", CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(new UTF8Encoding(false).GetBytes(text.ToString()), ct);
    }
}

/// <summary>Picks a file extension from the first bytes of an image; "bin" when unrecognised.</summary>
public static class ImageSniffer
{
    public static string Extension(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return "jpg";
        if (head.Length >= 8 && head[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "png";
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head.Slice(8, 4).SequenceEqual("WEBP"u8)) return "webp";
        if (head.Length >= 6 && (head[..6].SequenceEqual("GIF87a"u8) || head[..6].SequenceEqual("GIF89a"u8))) return "gif";
        if (head.Length >= 12 && head.Slice(4, 4).SequenceEqual("ftyp"u8))
            return head.Slice(8, 4).SequenceEqual("avif"u8) ? "avif" : "heic";
        return "bin";
    }
}
