using ApiReply = Vela.Api.Tests.Profiles.Reply;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Account;
using Vela.Api.Common;
using Vela.Api.Tests.Profiles;

namespace Vela.Api.Tests.Account;

/// <summary>An opened export archive: entry name to bytes.</summary>
public sealed class ExportZip(IReadOnlyDictionary<string, byte[]> entries)
{
    public IReadOnlyCollection<string> Names => entries.Keys.ToList();
    public byte[] Bytes(string name) => entries[name];
    public string Text(string name) => Encoding.UTF8.GetString(entries[name]);
    public JsonElement Json(string name) => JsonDocument.Parse(Text(name)).RootElement.Clone();
    public string AllText => string.Join("\n", entries.Where(e => e.Key.EndsWith(".json") || e.Key.EndsWith(".txt")).Select(e => Encoding.UTF8.GetString(e.Value)));

    public static ExportZip Open(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var map = new Dictionary<string, byte[]>();
        foreach (var e in archive.Entries)
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            map[e.FullName] = ms.ToArray();
        }
        return new ExportZip(map);
    }
}

public static class ExportKit
{
    public static async Task<ApiReply> Request(TestUser u) => await u.Post("/v1/me/export", new { });

    public static Task RunWorker(ProfileTestHost h) =>
        h.Services.GetRequiredService<ExportWorker>().RunOnceAsync(h.Clock.GetUtcNow(), CancellationToken.None);

    public static async Task<(Guid Id, ExportZip Zip)> RequestAndBuild(ProfileTestHost h, TestUser u)
    {
        var r = await Request(u);
        Assert.True(r.Body.GetProperty("success").GetBoolean(), r.Body.ToString());
        var id = r.Data.GetProperty("id").GetGuid();
        await RunWorker(h);
        return (id, ExportZip.Open(h.Blobs.Bytes($"exports/{u.Id}/{id}.zip")));
    }

    public static IReadOnlyList<SentEmail> EmailsTo(ProfileTestHost h, params string[] addresses) =>
        h.Services.GetRequiredService<FakeEmailSender>().Sent.Where(m => addresses.Contains(m.To, StringComparer.OrdinalIgnoreCase)).ToList();
}
