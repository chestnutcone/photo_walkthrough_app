using System.Net;
using Testcontainers.Azurite;
using Vela.Api.Images;

namespace Vela.Api.Tests.Images;

public sealed class AzuriteFixture : IAsyncLifetime
{
    private readonly AzuriteContainer _container = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest")
        .WithCommand("--skipApiVersionCheck").Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

public class AzureBlobStoreTests(AzuriteFixture azurite) : IClassFixture<AzuriteFixture>
{
    private const string PublicBase = "https://cdn.example.test/";

    private async Task<AzureBlobStore> Store()
    {
        var s = new AzureBlobStore(new StorageOptions { ConnectionString = azurite.ConnectionString, PublicBaseUrl = PublicBase });
        await s.EnsureContainersAsync();
        return s;
    }

    [Fact]
    public async Task Write_read_and_delete_prefix_round_trip()
    {
        var store = await Store();
        var id = Guid.NewGuid();
        await store.WriteAsync($"works/{id}/original", new MemoryStream([1, 2, 3]), "image/jpeg");
        await store.WriteAsync($"works/{id}/thumb.webp", new MemoryStream([4]), "image/webp");
        var other = Guid.NewGuid();
        await store.WriteAsync($"works/{other}/original", new MemoryStream([9]), "image/jpeg");

        await using (var s = await store.OpenReadAsync($"works/{id}/original"))
        {
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            Assert.Equal([1, 2, 3], ms.ToArray());
        }
        await store.DeletePrefixAsync($"works/{id}/");

        await Assert.ThrowsAsync<FileNotFoundException>(() => store.OpenReadAsync($"works/{id}/original"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => store.OpenReadAsync($"works/{id}/thumb.webp"));
        await using var kept = await store.OpenReadAsync($"works/{other}/original");
        Assert.Equal(1, kept.Length);
    }

    [Fact]
    public async Task Upload_url_is_write_only_for_one_blob_and_lives_at_most_15_minutes()
    {
        var store = await Store();
        var id = Guid.NewGuid();
        var url = store.CreateUploadUrl($"works/{id}/original", TimeSpan.FromHours(3));

        var expiry = DateTimeOffset.Parse(System.Web.HttpUtility.ParseQueryString(url.Query)["se"]!);
        Assert.True(expiry <= DateTimeOffset.UtcNow.AddMinutes(15).AddSeconds(5));
        Assert.Equal("cw", System.Web.HttpUtility.ParseQueryString(url.Query)["sp"]);

        using var http = new HttpClient();
        var put = new HttpRequestMessage(HttpMethod.Put, url) { Content = new ByteArrayContent([7, 7]) };
        put.Headers.Add("x-ms-blob-type", "BlockBlob");
        Assert.Equal(HttpStatusCode.Created, (await http.SendAsync(put)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(url)).StatusCode); // no read permission

        var elsewhere = new UriBuilder(url) { Path = url.AbsolutePath.Replace(id.ToString(), Guid.NewGuid().ToString()) }.Uri;
        var put2 = new HttpRequestMessage(HttpMethod.Put, elsewhere) { Content = new ByteArrayContent([7]) };
        put2.Headers.Add("x-ms-blob-type", "BlockBlob");
        Assert.NotEqual(HttpStatusCode.Created, (await http.SendAsync(put2)).StatusCode); // single-blob scope
    }

    [Fact]
    public async Task Public_url_is_the_cdn_base_plus_blob_name()
    {
        var store = await Store();
        Assert.Equal("https://cdn.example.test/avatars/u1/avatar-256.webp", store.PublicUrl("avatars/u1/avatar-256.webp").ToString());
    }

    [Fact]
    public async Task Blob_names_outside_known_containers_are_refused()
    {
        var store = await Store();
        Assert.Throws<ArgumentException>(() => store.CreateUploadUrl("secrets/x", TimeSpan.FromMinutes(5)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync("../x", new MemoryStream(), "x"));
    }
}
