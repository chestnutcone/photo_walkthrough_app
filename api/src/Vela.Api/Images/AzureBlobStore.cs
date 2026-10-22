using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace Vela.Api.Images;

public sealed class AzureBlobStore : IBlobStore
{
    public const string WorksContainer = "works";
    public const string AvatarsContainer = "avatars";
    private static readonly string[] Containers = [WorksContainer, AvatarsContainer];
    private static readonly TimeSpan MaxUploadTtl = TimeSpan.FromMinutes(15);

    private readonly BlobServiceClient _service;
    private readonly string _publicBase;

    public AzureBlobStore(StorageOptions options)
    {
        _service = new BlobServiceClient(options.ConnectionString);
        _publicBase = options.PublicBaseUrl.TrimEnd('/') + "/";
    }

    public async Task EnsureContainersAsync(CancellationToken ct = default)
    {
        foreach (var name in Containers)
            await _service.GetBlobContainerClient(name).CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
    }

    public Uri CreateUploadUrl(string blobName, TimeSpan ttl)
    {
        var (container, blob) = Split(blobName);
        var lifetime = ttl > MaxUploadTtl ? MaxUploadTtl : ttl;
        var client = _service.GetBlobContainerClient(container).GetBlobClient(blob);
        return client.GenerateSasUri(BlobSasPermissions.Create | BlobSasPermissions.Write, DateTimeOffset.UtcNow + lifetime);
    }

    public async Task WriteAsync(string name, Stream data, string contentType)
    {
        var (container, blob) = Split(name);
        await EnsureContainersAsync();
        await _service.GetBlobContainerClient(container).GetBlobClient(blob)
            .UploadAsync(data, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } });
    }

    public async Task<Stream> OpenReadAsync(string name)
    {
        var (container, blob) = Split(name);
        try
        {
            return await _service.GetBlobContainerClient(container).GetBlobClient(blob).OpenReadAsync();
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            throw new FileNotFoundException("blob not found", e);
        }
    }

    public async Task DeletePrefixAsync(string prefix)
    {
        var (container, rest) = Split(prefix);
        var client = _service.GetBlobContainerClient(container);
        await foreach (var item in client.GetBlobsAsync(BlobTraits.None, BlobStates.None, rest, CancellationToken.None))
            await client.DeleteBlobIfExistsAsync(item.Name);
    }

    public Uri PublicUrl(string name)
    {
        Split(name);
        return new Uri(_publicBase + name);
    }

    private static (string Container, string Blob) Split(string name)
    {
        var slash = name.IndexOf('/');
        if (slash <= 0 || slash == name.Length - 1 || !Containers.Contains(name[..slash])
            || name.Contains("..") || name.Contains('\\') || name.Contains('?') || name.Contains('#'))
            throw new ArgumentException("Blob name must be '{works|avatars}/<path>'.", nameof(name));
        return (name[..slash], name[(slash + 1)..]);
    }
}

/// <summary>Creates the storage containers in the background so a down store never blocks startup.</summary>
public sealed class BlobContainerInitializer(AzureBlobStore store, ILogger<BlobContainerInitializer> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await store.EnsureContainersAsync(stoppingToken); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning("Blob containers could not be ensured at startup ({Reason}); will retry on first write.", e.GetType().Name);
        }
    }
}
