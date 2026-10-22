using System.Net;
using Microsoft.EntityFrameworkCore;
using Vela.Api.Tests.Profiles;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Images;

[Collection(PostgresCollection.Name)]
public class AvatarTests(PostgresFixture pg) : IDisposable
{
    private readonly ProfileTestHost _host = new(pg);
    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Avatar_upload_requires_auth()
    {
        var r = await _host.Anonymous().PostAsync("/v1/me/avatar-upload", null);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Avatar_upload_returns_a_ticket_for_the_users_own_original_blob()
    {
        var u = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());

        var r = await u.Post("/v1/me/avatar-upload", new { });

        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Contains($"originals/avatars/{u.Id}", r.Data.GetProperty("uploadUrl").GetString());
        Assert.Equal(ProfileTestHost.Now.AddMinutes(15), r.Data.GetProperty("expiresAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Avatar_uploaded_generates_avatar_and_exposes_its_public_url()
    {
        var u = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        _host.Blobs.Put($"originals/avatars/{u.Id}", ImageFixtures.Jpeg(300, 300));

        var r = await u.Patch("/v1/me/profile", new { avatarUploaded = true });

        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal($"https://cdn.test/avatars/{u.Id}/avatar-256.webp", r.Data.GetProperty("profile").GetProperty("avatarUrl").GetString());
        Assert.True(_host.Blobs.Exists($"avatars/{u.Id}/avatar-256.webp"));
        Assert.False(_host.Blobs.Exists($"originals/avatars/{u.Id}")); // raw upload removed after success
        await using var db = _host.Db();
        Assert.Equal($"avatars/{u.Id}/avatar-256.webp", (await db.Profiles.SingleAsync(p => p.UserId == u.Id)).AvatarBlob);
    }

    [Fact]
    public async Task Avatar_uploaded_with_a_bad_image_is_invalid_image_and_changes_nothing()
    {
        var u = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        _host.Blobs.Put($"originals/avatars/{u.Id}", "not an image"u8.ToArray());

        var r = await u.Patch("/v1/me/profile", new { avatarUploaded = true, bio = "should not stick" });

        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Equal("invalid_image", r.ErrorCode);
        Assert.True(_host.Blobs.Exists($"originals/avatars/{u.Id}")); // kept after a failed generation
        await using var db = _host.Db();
        var p = await db.Profiles.SingleAsync(x => x.UserId == u.Id);
        Assert.Null(p.AvatarBlob);
        Assert.Equal("", p.Bio);
    }

    [Fact]
    public async Task Avatar_uploaded_without_an_upload_is_invalid_image()
    {
        var u = await _host.SignInOnboarded(ProfileTestHost.UniqueHandle());
        var r = await u.Patch("/v1/me/profile", new { avatarUploaded = true });
        Assert.Equal("invalid_image", r.ErrorCode);
    }

    [Fact]
    public async Task Public_artist_view_carries_the_avatar_url()
    {
        var h = ProfileTestHost.UniqueHandle();
        var u = await _host.SignInOnboarded(h);
        _host.Blobs.Put($"originals/avatars/{u.Id}", ImageFixtures.Png(100, 100));
        await u.Patch("/v1/me/profile", new { avatarUploaded = true });

        var r = await u.Get($"/v1/artists/{h}");

        Assert.Equal($"https://cdn.test/avatars/{u.Id}/avatar-256.webp", r.Data.GetProperty("avatarUrl").GetString());
    }
}
