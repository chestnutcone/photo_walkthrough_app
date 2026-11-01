using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Vela.Api.Account;
using Vela.Api.Admin;
using Vela.Api.Data.Entities;
using Vela.Api.Images;
using Vela.Api.Tests.Support;
using Vela.Api.Tests.Works;
using static Vela.Api.Tests.Admin.ModerationKit;

namespace Vela.Api.Tests.Admin;

/// <summary>
/// S1b-3: at publish the upload is copied to a server-owned sealed blob (with its SHA-256 on the row) and the writable upload is
/// deleted. Everything after publish reads the sealed copy, so writing to the old upload URL changes nothing.
/// </summary>
[Collection(PostgresCollection.Name)]
public class SealedOriginalTests(PostgresFixture pg) : IAsyncLifetime
{
    private AdminSite _s = null!;
    private Guid _admin;
    public async Task InitializeAsync() { _s = await AdminSite.Create(pg, isolated: true); _admin = await _s.NewAdminUser(); }
    public Task DisposeAsync() { _s.Dispose(); return Task.CompletedTask; }

    /// <summary>The benign swap-in: all green, so any pixel taken from it is easy to spot against the red/blue upload.</summary>
    private static byte[] Green() => ImageFixtures.Encode(ImageFixtures.Halves(400, 300, SKColors.Lime, SKColors.Lime), SKEncodedImageFormat.Jpeg);

    private static bool LooksGreen(byte[] webp)
    {
        using var bmp = SKBitmap.Decode(webp);
        var left = bmp.GetPixel(bmp.Width / 4, bmp.Height / 2);
        return left.Green > 180 && left.Red < 100;
    }

    private async Task<(Guid Work, byte[] Uploaded)> PublishedWithKnownUpload()
    {
        var owner = await _s.NewUser();
        var id = await WorkTestKit.ReadyDraft(_s.Host, owner);
        var uploaded = _s.Host.Blobs.Bytes(BlobNames.WorkOriginal(id));
        Assert.True((await owner.Post($"/v1/works/{id}/publish", new { })).Body.GetProperty("success").GetBoolean());
        return (id, uploaded);
    }

    [Fact]
    public async Task Publish_seals_the_upload_records_its_hash_and_deletes_the_writable_upload()
    {
        var (work, uploaded) = await PublishedWithKnownUpload();

        var w = await WorkTestKit.Load(_s.Host, work);
        Assert.NotNull(w.SealedOriginalBlob);
        Assert.StartsWith($"originals/sealed/{work}/", w.SealedOriginalBlob);
        Assert.Equal(uploaded, _s.Host.Blobs.Bytes(w.SealedOriginalBlob!));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(uploaded)), w.SealedOriginalSha256);
        Assert.False(_s.Host.Blobs.Exists(BlobNames.WorkOriginal(work)));
        Assert.All(Variants(work), v => Assert.True(_s.Host.Blobs.Exists(v)));
    }

    [Fact]
    public async Task Writing_to_the_upload_url_after_publish_changes_nothing_that_is_scanned_reviewed_held_or_restored()
    {
        var (work, uploaded) = await PublishedWithKnownUpload();
        var sealedName = (await WorkTestKit.Load(_s.Host, work)).SealedOriginalBlob!;

        // The attacker's SAS still allows Create|Write on the old upload name for up to 15 minutes.
        _s.Host.Blobs.Put(BlobNames.WorkOriginal(work), Green());
        await _s.RunPipeline();
        Assert.Equal(WorkStatus.Published, (await WorkTestKit.Load(_s.Host, work)).Status);
        Assert.False(_s.Host.Blobs.Exists(BlobNames.WorkOriginal(work))); // the sweep removes the resurrected upload again
        _s.Host.Blobs.Put(BlobNames.WorkOriginal(work), Green());

        // Admin report page: the image shown is the sealed original.
        var report = await _s.Report(await _s.NewUser(), "work", work, "illegal_content");
        var page = await (await _s.AdminBrowser()).Get($"/admin/reports/{report}");
        Assert.Contains(sealedName["originals/".Length..], page.Body);
        Assert.Contains("Sealed original", page.Body);

        // Restore regenerates from the sealed copy, not from the swapped upload.
        using (var scope = _s.Host.Scope())
        {
            var lifecycle = scope.ServiceProvider.GetRequiredService<IWorkVariantLifecycle>();
            await lifecycle.TakeDownAsync(work);
            await lifecycle.RestoreAsync(work);
        }
        Assert.All(Variants(work), v => Assert.False(LooksGreen(_s.Host.Blobs.Bytes(v)), v));

        // The evidence hold copies the sealed bytes.
        var result = await _s.MarkIllegal(_admin, report);
        Assert.Equal(ModerationOutcome.Ok, result.Outcome);
        Assert.Equal(uploaded, _s.Host.Blobs.Bytes($"evidence/{result.HoldId}/original"));
        Assert.Equal(uploaded, _s.Host.Blobs.Bytes(sealedName));
    }

    [Fact]
    public async Task A_sealed_original_that_no_longer_matches_its_hash_is_blocked_instead_of_published()
    {
        var (work, _) = await PublishedWithKnownUpload();
        _s.Host.Blobs.Put((await WorkTestKit.Load(_s.Host, work)).SealedOriginalBlob!, Green());

        await _s.RunPipeline();

        Assert.Equal(WorkStatus.Blocked, (await WorkTestKit.Load(_s.Host, work)).Status);
        await using var db = _s.Host.Db();
        Assert.True(await db.Reports.AnyAsync(r => r.Source == "auto_flag" && r.TargetId == work && r.Reason.StartsWith("auto:sealed_original_mismatch:")));
    }

    [Fact]
    public async Task The_export_carries_the_sealed_original_not_a_later_upload()
    {
        var owner = await _s.NewUser();
        var id = await WorkTestKit.ReadyDraft(_s.Host, owner);
        await owner.Post($"/v1/works/{id}/publish", new { });
        var sealedName = (await WorkTestKit.Load(_s.Host, id)).SealedOriginalBlob!;
        _s.Host.Blobs.Put(BlobNames.WorkOriginal(id), Green());

        using var scope = _s.Host.Scope();
        await scope.ServiceProvider.GetRequiredService<ExportArchiveBuilder>().BuildAsync(owner.Id, $"exports/{owner.Id}/t.zip", CancellationToken.None);

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(_s.Host.Blobs.Bytes($"exports/{owner.Id}/t.zip")));
        var entry = zip.Entries.Single(e => e.FullName.StartsWith($"images/works/{id}/original", StringComparison.Ordinal));
        using var ms = new MemoryStream();
        await entry.Open().CopyToAsync(ms);
        Assert.Equal(_s.Host.Blobs.Bytes(sealedName), ms.ToArray());
    }

    [Fact]
    public async Task Account_deletion_removes_the_sealed_original_and_any_resurrected_upload()
    {
        var owner = await _s.NewUser();
        var id = await WorkTestKit.ReadyDraft(_s.Host, owner);
        await owner.Post($"/v1/works/{id}/publish", new { });
        await _s.RunPipeline();
        _s.Host.Blobs.Put(BlobNames.WorkOriginal(id), Green());

        await owner.Delete("/v1/me");
        await _s.Host.Services.GetRequiredService<DeletionWorker>().RunOnceAsync(_s.Host.Clock.GetUtcNow(), CancellationToken.None);

        Assert.DoesNotContain(_s.Host.Blobs.Names, n => n.Contains(id.ToString()));
    }

    [Fact]
    public async Task Deleting_a_published_work_removes_the_sealed_original()
    {
        var owner = await _s.NewUser();
        var id = await WorkTestKit.ReadyDraft(_s.Host, owner);
        await owner.Post($"/v1/works/{id}/publish", new { });
        await _s.RunPipeline();

        await owner.Delete($"/v1/works/{id}");

        Assert.DoesNotContain(_s.Host.Blobs.Names, n => n.Contains(id.ToString()));
    }
}
