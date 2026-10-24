using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Vela.Api.Images;

/// <summary>Reads a container's anonymous-access level; null when the container does not exist.</summary>
public interface IContainerAccessReader
{
    Task<PublicAccessType?> GetAsync(string container, CancellationToken ct);
}

public sealed class AzureContainerAccessReader(BlobServiceClient service) : IContainerAccessReader
{
    public async Task<PublicAccessType?> GetAsync(string container, CancellationToken ct)
    {
        try { return (await service.GetBlobContainerClient(container).GetPropertiesAsync(cancellationToken: ct)).Value.PublicAccess ?? PublicAccessType.None; }
        catch (RequestFailedException e) when (e.Status == 404) { return null; }
    }
}

/// <summary>
/// The storage account allows container-level public access (works/avatars need it), so a portal change or a container
/// created public would expose private data. At startup, fails if originals, exports, evidence or the Data Protection
/// container is anything but private, or if a public container lists its contents (Container level).
/// An unreachable store only logs a warning, like the container initializer.
/// </summary>
public sealed class ContainerAccessGuard : IHostedService
{
    private static readonly string[] PublicContainers = [BlobNames.Works, BlobNames.Avatars];
    private readonly IContainerAccessReader _reader;
    private readonly ILogger<ContainerAccessGuard> _log;

    public ContainerAccessGuard(IContainerAccessReader reader, StorageOptions options, string dataProtectionContainer, ILogger<ContainerAccessGuard> log)
    {
        _reader = reader;
        _log = log;
        PrivateContainers = [options.OriginalsContainer, options.ExportsContainer, options.EvidenceContainer, dataProtectionContainer];
    }

    public IReadOnlyList<string> PrivateContainers { get; }

    public async Task StartAsync(CancellationToken ct)
    {
        var violations = new List<string>();
        try
        {
            foreach (var c in PrivateContainers)
                if (await _reader.GetAsync(c, ct) is { } level && level != PublicAccessType.None) violations.Add($"{c} ({level})");
            foreach (var c in PublicContainers)
                if (await _reader.GetAsync(c, ct) == PublicAccessType.BlobContainer) violations.Add($"{c} (BlobContainer lists its contents)");
        }
        catch (Exception e) when (e is RequestFailedException or HttpRequestException or TimeoutException or AggregateException)
        {
            _log.LogWarning("Container access check skipped, storage unreachable ({Reason}).", e.GetType().Name);
            return;
        }
        if (violations.Count > 0)
            throw new InvalidOperationException("Blob containers have unsafe anonymous access: " + string.Join(", ", violations) +
                ". Set the container's public access level to private (None) for originals/exports/evidence/dataprotection and Blob for works/avatars.");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
