using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Vela.Api.Images;

namespace Vela.Api.Tests.Images;

public class ContainerAccessGuardTests
{
    private sealed class FakeReader(Dictionary<string, PublicAccessType?> access, Exception? fail = null) : IContainerAccessReader
    {
        public Task<PublicAccessType?> GetAsync(string container, CancellationToken ct)
        {
            if (fail is not null) throw fail;
            return Task.FromResult(access.GetValueOrDefault(container));
        }
    }

    private static readonly string[] Private = ["originals", "exports", "evidence", "dataprotection"];

    private static Dictionary<string, PublicAccessType?> Good() => new()
    {
        ["works"] = PublicAccessType.Blob, ["avatars"] = PublicAccessType.Blob,
        ["originals"] = PublicAccessType.None, ["exports"] = PublicAccessType.None,
        ["evidence"] = PublicAccessType.None, ["dataprotection"] = PublicAccessType.None,
    };

    private static ContainerAccessGuard Guard(FakeReader r) =>
        new(r, new StorageOptions(), "dataprotection", NullLogger<ContainerAccessGuard>.Instance);

    [Fact]
    public async Task Correct_levels_pass() => await Guard(new FakeReader(Good())).StartAsync(default);

    [Fact]
    public async Task Missing_containers_pass_because_they_are_created_private()
        => await Guard(new FakeReader([])).StartAsync(default);

    [Theory]
    [InlineData("originals", PublicAccessType.Blob)]
    [InlineData("exports", PublicAccessType.BlobContainer)]
    [InlineData("evidence", PublicAccessType.Blob)]
    [InlineData("dataprotection", PublicAccessType.BlobContainer)]
    public async Task A_public_private_container_fails_startup_naming_it(string container, PublicAccessType level)
    {
        var access = Good();
        access[container] = level;
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Guard(new FakeReader(access)).StartAsync(default));
        Assert.Contains(container, e.Message);
    }

    [Theory]
    [InlineData("works")]
    [InlineData("avatars")]
    public async Task Container_level_listing_on_a_public_container_fails(string container)
    {
        var access = Good();
        access[container] = PublicAccessType.BlobContainer;
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Guard(new FakeReader(access)).StartAsync(default));
        Assert.Contains(container, e.Message);
    }

    [Fact]
    public async Task Renamed_private_containers_are_checked_by_their_physical_name()
    {
        var access = Good();
        access["secret-originals"] = PublicAccessType.Blob;
        var guard = new ContainerAccessGuard(new FakeReader(access), new StorageOptions { OriginalsContainer = "secret-originals" },
            "dataprotection", NullLogger<ContainerAccessGuard>.Instance);
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => guard.StartAsync(default));
        Assert.Contains("secret-originals", e.Message);
    }

    [Fact]
    public async Task An_unreachable_store_does_not_block_startup() =>
        await Guard(new FakeReader([], new HttpRequestException("down"))).StartAsync(default);

    [Fact]
    public void Every_private_container_is_covered() =>
        Assert.Equal(Private.Order(), Guard(new FakeReader([])).PrivateContainers.Order());
}
