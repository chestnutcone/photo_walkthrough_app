using Microsoft.EntityFrameworkCore;
using Vela.Api.Data.Entities;
using Vela.Api.Images;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Works;

/// <summary>Shared helpers for work tests: drafts with real uploads, chapter payloads, direct DB edits.</summary>
public static class WorkTestKit
{
    public static object Chapter(double x = 0.5, double y = 0.5, double? zoom = null, string title = "Chapter", string body = "Body", string? meta = null, string? altText = null) =>
        new { x, y, zoom, title, body, meta, altText };

    public static object Update(string title = "Title", IEnumerable<object>? chapters = null, string? intro = null, string? altText = null) =>
        new { title, intro, altText, chapters = (chapters ?? [Chapter()]).ToArray() };

    public static async Task<Guid> Draft(ProfileTestHost host, TestUser u, bool withImage = true)
    {
        var r = await u.Post("/v1/works", new { contentType = "image/jpeg" });
        var id = r.Data.GetProperty("workId").GetGuid();
        if (withImage) host.Blobs.Put(BlobNames.WorkOriginal(id), ImageFixtures.Jpeg(400, 300));
        return id;
    }

    /// <summary>A draft with a valid upload and one chapter, ready to publish.</summary>
    public static async Task<Guid> ReadyDraft(ProfileTestHost host, TestUser u, string title = "Title", IEnumerable<object>? chapters = null)
    {
        var id = await Draft(host, u);
        var r = await u.Put($"/v1/works/{id}", Update(title, chapters));
        Assert.True(r.Body.GetProperty("success").GetBoolean(), r.Body.ToString());
        return id;
    }

    public static async Task<Work> Load(ProfileTestHost host, Guid id)
    {
        await using var db = host.Db();
        return await db.Works.AsNoTracking().SingleAsync(w => w.Id == id);
    }

    /// <summary>The original every reader uses after publish: the sealed copy (S1b-3), or the upload for a draft.</summary>
    public static async Task<string> OriginalOf(ProfileTestHost host, Guid id) =>
        BlobNames.OriginalOf(id, (await Load(host, id)).SealedOriginalBlob);

    public static async Task Modify(ProfileTestHost host, Guid id, Action<Work> change)
    {
        await using var db = host.Db();
        var w = await db.Works.SingleAsync(x => x.Id == id);
        change(w);
        await db.SaveChangesAsync();
    }

    public static async Task MakeAdmin(ProfileTestHost host, Guid userId)
    {
        await using var db = host.Db();
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsAdmin, true));
    }
}
