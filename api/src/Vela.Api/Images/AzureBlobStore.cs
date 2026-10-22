using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace Vela.Api.Images;

public sealed class AzureBlobStore : IBlobStore
{
    private static readonly TimeSpan MaxUploadTtl = TimeSpan.FromMinutes(15);

    private readonly BlobServiceClient _service;
    private readonly string _publicBase;
    private readonly string _originalsContainer;
    private readonly string _exportsContainer;

    public AzureBlobStore(StorageOptions options)
    {
        _service = new BlobServiceClient(options.ConnectionString);
        _publicBase = options.PublicBaseUrl.TrimEnd('/') + "/";
        _originalsContainer = options.OriginalsContainer;
        _exportsContainer = options.ExportsContainer;
    }

    public async Task EnsureContainersAsync(CancellationToken ct = default)
    {
        foreach (var name in new[] { BlobNames.Works, BlobNames.Avatars, _originalsContainer, _exportsContainer })
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
        var (container, _) = Split(name);
        if (container == _originalsContainer || container == _exportsContainer)
            throw new ArgumentException("Originals are private and have no public URL.", nameof(name));
        return new Uri(_publicBase + name);
    }

    private (string Container, string Blob) Split(string name)
    {
        var slash = name.IndexOf('/');
        if (slash <= 0 || slash == name.Length - 1
            || name.Contains("..") || name.Contains('\\') || name.Contains('?') || name.Contains('#'))
            throw new ArgumentException("Blob name must be '{originals|works|avatars|exports}/<path>'.", nameof(name));
        var logical = name[..slash];
        var container = logical switch
        {
            BlobNames.Originals => _originalsContainer,
            BlobNames.Exports => _exportsContainer,
            BlobNames.Works or BlobNames.Avatars => logical,
            _ => throw new ArgumentException("Blob name must be '{originals|works|avatars|exports}/<path>'.", nameof(name)),
        };
        return (container, name[(slash + 1)..]);
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
