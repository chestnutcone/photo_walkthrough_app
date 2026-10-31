using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Moderation;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Images;

/// <summary>
/// FINALAPI I6: avatars are served publicly on every card, so they are scanned like work images before anything is written to
/// the public container. Unsafe: refused and queued against the account. Scanner down: fail closed (503 busy, upload kept).
/// </summary>
[Collection(PostgresCollection.Name)]
public class AvatarScanTests(PostgresFixture pg) : IDisposable
{
    private readonly ProfileTestHost _host = new(pg);
    private FakeContentSafety Safety => _host.Services.GetRequiredService<FakeContentSafety>();
    public void Dispose() => _host.Dispose();

    private async Task<byte[]> Bytes(string name)
    {
        await using var s = await _host.Blobs.OpenReadAsync(name);
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms);
        return ms.ToArray();
    }

    private async Task<TestUser> WithUpload()
    {
        var u = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        _host.Blobs.Put($"originals/avatars/{u.Id}", ImageFixtures.Jpeg(300, 300));
        return u;
    }

    [Fact]
    public async Task A_clean_avatar_is_scanned_once_and_published()
    {
        Safety.ImageSeverity = 2;
        var before = Safety.ImageScans;
        var u = await WithUpload();

        var r = await u.Patch("/v1/me/profile", new { avatarUploaded = true });

        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal(before + 1, Safety.ImageScans);
        Assert.True(_host.Blobs.Exists($"avatars/{u.Id}/avatar-256.webp"));
    }

    [Fact]
    public async Task An_unsafe_avatar_is_refused_never_reaches_the_public_container_and_is_queued_for_review()
    {
        var u = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        Safety.ImageSeverity = 0;
        _host.Blobs.Put($"originals/avatars/{u.Id}", ImageFixtures.Jpeg(300, 300));
        Assert.Equal(HttpStatusCode.OK, (await u.Patch("/v1/me/profile", new { avatarUploaded = true })).Status);
        var good = await Bytes($"avatars/{u.Id}/avatar-256.webp");

        Safety.ImageSeverity = 6;
        _host.Blobs.Put($"originals/avatars/{u.Id}", ImageFixtures.Png(320, 320));
        var r = await u.Patch("/v1/me/profile", new { avatarUploaded = true, bio = "should not stick" });

        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Equal("invalid_image", r.ErrorCode);
        Assert.Equal(good, await Bytes($"avatars/{u.Id}/avatar-256.webp")); // the previous avatar is untouched
        Assert.False(_host.Blobs.Exists($"originals/avatars/{u.Id}")); // the upload cannot be replayed
        await using var db = _host.Db();
        Assert.Equal("", (await db.Profiles.SingleAsync(p => p.UserId == u.Id)).Bio);
        var flag = await db.Reports.AsNoTracking().SingleAsync(x => x.TargetType == "user" && x.TargetId == u.Id);
        Assert.Equal("auto_flag", flag.Source);
        Assert.Equal("auto:unsafe_avatar:6", flag.Reason);
    }

    [Fact]
    public async Task When_the_scanner_is_down_the_avatar_fails_closed_with_busy_and_the_upload_is_kept_for_a_retry()
    {
        var u = await WithUpload();
        Safety.Throw = true;
        try
        {
            var r = await u.Patch("/v1/me/profile", new { avatarUploaded = true });

            Assert.Equal(HttpStatusCode.ServiceUnavailable, r.Status);
            Assert.Equal("busy", r.ErrorCode);
            Assert.False(_host.Blobs.Exists($"avatars/{u.Id}/avatar-256.webp"));
            Assert.True(_host.Blobs.Exists($"originals/avatars/{u.Id}"));
        }
        finally { Safety.Throw = false; }

        Assert.Equal(HttpStatusCode.OK, (await u.Patch("/v1/me/profile", new { avatarUploaded = true })).Status);
    }
}
