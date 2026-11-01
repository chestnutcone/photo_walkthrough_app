using System.Net;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Vela.Api.Images;

namespace Vela.Api.Tests.Images;

public class EvidenceContainerTests(AzuriteFixture azurite) : IClassFixture<AzuriteFixture>
{
    private static readonly Microsoft.Extensions.Hosting.Internal.HostingEnvironment Env = new() { EnvironmentName = "Test" };

    private async Task<AzureBlobStore> Store()
    {
        var s = new AzureBlobStore(new StorageOptions { ConnectionString = azurite.ConnectionString, PublicBaseUrl = "https://cdn.example.test/" });
        await s.EnsureContainersAsync();
        return s;
    }

    [Fact]
    public async Task The_evidence_container_is_created_private_and_has_no_public_url()
    {
        var store = await Store();
        var id = Guid.NewGuid();
        await store.WriteAsync($"evidence/{id}/original", new MemoryStream([1, 2]), "image/jpeg");

        var service = new BlobServiceClient(azurite.ConnectionString);
        Assert.Equal(PublicAccessType.None, (await service.GetBlobContainerClient("evidence").GetAccessPolicyAsync()).Value.BlobPublicAccess);
        Assert.Throws<ArgumentException>(() => store.PublicUrl($"evidence/{id}/original"));
        using var http = new HttpClient();
        var bare = new Uri($"{service.Uri.GetLeftPart(UriPartial.Authority)}/{service.AccountName}/evidence/{id}/original");
        Assert.NotEqual(HttpStatusCode.OK, (await http.GetAsync(bare)).StatusCode);
    }

    [Fact]
    public async Task Evidence_read_urls_are_read_only_and_delete_by_prefix_removes_the_copy()
    {
        var store = await Store();
        var id = Guid.NewGuid();
        await store.WriteAsync($"evidence/{id}/original", new MemoryStream([7, 8]), "image/jpeg");
        await store.WriteAsync($"evidence/{id}/variants/thumb.webp", new MemoryStream([9]), "image/webp");

        var url = store.CreateReadUrl($"evidence/{id}/original", TimeSpan.FromMinutes(5));
        using var http = new HttpClient();
        Assert.Equal([7, 8], await http.GetByteArrayAsync(url));
        Assert.True(DateTimeOffset.Parse(System.Web.HttpUtility.ParseQueryString(url.Query)["se"]!) <= DateTimeOffset.UtcNow.AddMinutes(5).AddSeconds(5));

        await store.DeletePrefixAsync($"evidence/{id}/");
        await Assert.ThrowsAsync<FileNotFoundException>(() => store.OpenReadAsync($"evidence/{id}/original"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => store.OpenReadAsync($"evidence/{id}/variants/thumb.webp"));
    }

    [Fact]
    public void The_evidence_container_name_is_configurable_defaults_to_evidence_and_may_not_be_a_public_container()
    {
        Assert.Equal("evidence", StorageOptions.Load(new ConfigurationBuilder().Build(), Env).EvidenceContainer);
        var custom = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Containers:Evidence"] = "vela-evidence" }).Build();
        Assert.Equal("vela-evidence", StorageOptions.Load(custom, Env).EvidenceContainer);
        foreach (var bad in new[] { "works", "avatars" })
        {
            var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Containers:Evidence"] = bad }).Build();
            Assert.Contains("Storage:Containers:Evidence", Assert.Throws<InvalidOperationException>(() => StorageOptions.Load(cfg, Env)).Message);
        }
        var same = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Containers:Evidence"] = "x", ["Storage:Containers:Exports"] = "x" }).Build();
        Assert.Throws<InvalidOperationException>(() => StorageOptions.Load(same, Env));
    }
}
