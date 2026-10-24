using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Images;
using Vela.Api.Tests.Support;
using static Vela.Api.Tests.Security.SecurityWorld;

namespace Vela.Api.Tests.Security;

/// <summary>Shared-access URLs handed to clients: one blob, write-only, short-lived, https. The signing key is generated per run, never written down.</summary>
[Collection(PostgresCollection.Name)]
public class UploadUrlTests(PostgresFixture pg)
{
    private static AzureBlobStore NewStore() => new(new StorageOptions
    {
        ConnectionString = "DefaultEndpointsProtocol=https;AccountName=velasectest;AccountKey=" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            + ";EndpointSuffix=core.windows.net",
        PublicBaseUrl = "https://cdn.example.test/",
    });

    private static void AssertScopedToOneBlob(Uri url, string expectedContainer, string expectedBlobPrefix, string permissions)
    {
        var q = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("https", url.Scheme);
        Assert.Equal("velasectest.blob.core.windows.net", url.Host);
        Assert.Equal("b", q["sr"]);                          // a single blob, not a container
        Assert.Equal(permissions, q["sp"]);                  // exactly these permissions
        Assert.False(string.IsNullOrEmpty(q["sig"]));
        Assert.StartsWith($"/{expectedContainer}/{expectedBlobPrefix}", url.AbsolutePath);
        Assert.DoesNotContain("..", url.AbsolutePath);
        var expiry = DateTimeOffset.Parse(q["se"]!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(expiry, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(15).AddSeconds(5));
        Assert.Equal("https", q["spr"]);                     // never over plain http
    }

    [Fact]
    public void Upload_urls_are_scoped_to_one_blob_and_expire_within_15_minutes()
    {
        var store = NewStore();
        var id = Guid.NewGuid();

        // Even when asked for hours, the lifetime is capped at 15 minutes.
        var work = store.CreateUploadUrl($"originals/works/{id}", TimeSpan.FromHours(6));
        var avatar = store.CreateUploadUrl($"originals/avatars/{id}", TimeSpan.FromDays(1));

        AssertScopedToOneBlob(work, "originals", $"works/{id}", "cw");
        AssertScopedToOneBlob(avatar, "originals", $"avatars/{id}", "cw");
    }

    [Fact]
    public void Read_urls_are_read_only_one_blob_and_expire_within_15_minutes()
    {
        var url = NewStore().CreateReadUrl($"exports/{Guid.NewGuid()}/data.zip", TimeSpan.FromDays(3));
        AssertScopedToOneBlob(url, "exports", "", "r");
    }

    [Fact]
    public void Blob_names_that_could_widen_the_scope_are_refused()
    {
        var store = NewStore();
        foreach (var name in new[] { "originals/../works/x", "originals/", "originals", "../x", "originals/a?sr=c", "originals/a#b", "other/x", "originals\\x" })
            Assert.Throws<ArgumentException>(() => store.CreateUploadUrl(name, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task The_endpoints_return_such_urls_and_an_expiry_no_later_than_15_minutes()
    {
        var store = NewStore();
        var w = await SecurityWorld.CreateWithServicesAsync(pg, s => s.AddSingleton<IBlobStore>(store));
        var before = w.Host.Clock.GetUtcNow();

        var draft = await w.Alice.Post("/v1/works", new { contentType = "image/jpeg" });
        var avatar = await w.Alice.Post("/v1/me/avatar-upload", new { });

        Assert.Equal(HttpStatusCode.OK, draft.Status);
        Assert.Equal(HttpStatusCode.OK, avatar.Status);
        var workId = draft.Data.GetProperty("workId").GetGuid();
        AssertScopedToOneBlob(new Uri(draft.Data.GetProperty("uploadUrl").GetString()!), "originals", $"works/{workId}", "cw");
        AssertScopedToOneBlob(new Uri(avatar.Data.GetProperty("uploadUrl").GetString()!), "originals", $"avatars/{w.Alice.Id}", "cw");
        foreach (var r in new[] { draft, avatar })
            Assert.True(r.Data.GetProperty("expiresAt").GetDateTimeOffset() <= before.AddMinutes(15).AddSeconds(5));
        w.Dispose();
    }

    [Fact]
    public async Task One_users_upload_url_never_points_at_another_users_blob()
    {
        var store = NewStore();
        var w = await SecurityWorld.CreateWithServicesAsync(pg, s => s.AddSingleton<IBlobStore>(store));
        var bobAvatar = await w.Bob.Post("/v1/me/avatar-upload", new { });
        var aliceAvatar = await w.Alice.Post("/v1/me/avatar-upload", new { });
        var b = new Uri(bobAvatar.Data.GetProperty("uploadUrl").GetString()!).AbsolutePath;
        var a = new Uri(aliceAvatar.Data.GetProperty("uploadUrl").GetString()!).AbsolutePath;
        Assert.Contains(w.Bob.Id.ToString(), b);
        Assert.DoesNotContain(w.Alice.Id.ToString(), b);
        Assert.Contains(w.Alice.Id.ToString(), a);
        w.Dispose();
    }
}
