using System.Net;
using Microsoft.Extensions.Configuration;
using Vela.Api.Images;
using Vela.Api.Tests.Images;

namespace Vela.Api.Tests.Account;

public class ExportBlobStoreTests(AzuriteFixture azurite) : IClassFixture<AzuriteFixture>
{
    private async Task<AzureBlobStore> Store()
    {
        var s = new AzureBlobStore(new StorageOptions { ConnectionString = azurite.ConnectionString, PublicBaseUrl = "https://cdn.example.test/" });
        await s.EnsureContainersAsync();
        return s;
    }

    [Fact]
    public async Task Read_url_is_read_only_single_blob_and_clamped_to_15_minutes()
    {
        var store = await Store();
        var user = Guid.NewGuid();
        var name = BlobNames.ExportArchive(user, Guid.NewGuid());
        await store.WriteAsync(name, new MemoryStream([5, 6, 7]), "application/zip");
        await store.WriteAsync(BlobNames.ExportArchive(user, Guid.NewGuid()), new MemoryStream([1]), "application/zip");

        var url = store.CreateReadUrl(name, TimeSpan.FromHours(5));

        var query = System.Web.HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("r", query["sp"]);
        Assert.True(DateTimeOffset.Parse(query["se"]!) <= DateTimeOffset.UtcNow.AddMinutes(15).AddSeconds(5));
        using var http = new HttpClient();
        Assert.Equal([5, 6, 7], await http.GetByteArrayAsync(url));
        var put = new HttpRequestMessage(HttpMethod.Put, url) { Content = new ByteArrayContent([9]) };
        put.Headers.Add("x-ms-blob-type", "BlockBlob");
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(put)).StatusCode);
        var other = new UriBuilder(url) { Path = url.AbsolutePath.Replace(name.Split('/')[2], Guid.NewGuid() + ".zip") }.Uri;
        Assert.NotEqual(HttpStatusCode.OK, (await http.GetAsync(other)).StatusCode);
        var bare = new UriBuilder(url) { Query = "" }.Uri;
        Assert.NotEqual(HttpStatusCode.OK, (await http.GetAsync(bare)).StatusCode); // the container is not public
    }

    [Fact]
    public async Task Exports_have_no_public_url()
    {
        var store = await Store();
        Assert.Throws<ArgumentException>(() => store.PublicUrl(BlobNames.ExportArchive(Guid.NewGuid(), Guid.NewGuid())));
    }

    [Fact]
    public async Task Exports_container_is_private_and_deleted_by_prefix()
    {
        var store = await Store();
        var user = Guid.NewGuid();
        await store.WriteAsync(BlobNames.ExportArchive(user, Guid.NewGuid()), new MemoryStream([1]), "application/zip");
        var service = new Azure.Storage.Blobs.BlobServiceClient(azurite.ConnectionString);
        Assert.Equal(Azure.Storage.Blobs.Models.PublicAccessType.None, (await service.GetBlobContainerClient("exports").GetAccessPolicyAsync()).Value.BlobPublicAccess);

        await store.DeletePrefixAsync(BlobNames.ExportsPrefix(user));

        var names = new List<string>();
        await foreach (var b in service.GetBlobContainerClient("exports").GetBlobsAsync(Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, user + "/", default)) names.Add(b.Name);
        Assert.Empty(names);
    }

    [Fact]
    public void The_exports_container_name_is_configurable_and_defaults_to_exports()
    {
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Test" };
        var defaults = StorageOptions.Load(new ConfigurationBuilder().Build(), env);
        var custom = StorageOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Containers:Exports"] = "vela-exports" }).Build(), env);

        Assert.Equal("exports", defaults.ExportsContainer);
        Assert.Equal("vela-exports", custom.ExportsContainer);
    }
}
