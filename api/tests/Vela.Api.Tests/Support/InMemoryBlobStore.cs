using System.Collections.Concurrent;
using Vela.Api.Images;

namespace Vela.Api.Tests.Support;

/// <summary>Dictionary-backed blob store for tests that do not need Azurite.</summary>
public sealed class InMemoryBlobStore : IBlobStore
{
    private readonly ConcurrentDictionary<string, byte[]> _blobs = new();

    public IReadOnlyCollection<string> Names => _blobs.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
    public bool Exists(string name) => _blobs.ContainsKey(name);
    public byte[] Bytes(string name) => _blobs[name];
    public void Put(string name, byte[] data) => _blobs[name] = data;

    public Uri CreateUploadUrl(string blobName, TimeSpan ttl) => new($"https://blob.test/{blobName}?sig=test&ttl={(int)ttl.TotalSeconds}");

    public async Task WriteAsync(string name, Stream data, string contentType)
    {
        if (FailWrites?.Invoke(name) == true) throw new IOException("write failed");
        using var ms = new MemoryStream();
        await data.CopyToAsync(ms);
        _blobs[name] = ms.ToArray();
    }

    /// <summary>Lets a test make selected writes fail (for example the export archive).</summary>
    public Func<string, bool>? FailWrites { get; set; }

    public Uri CreateReadUrl(string name, TimeSpan ttl) =>
        new($"https://blob.test/{name}?sp=r&ttl={(int)Math.Min(ttl.TotalSeconds, 900)}");

    public Task<Stream> OpenReadAsync(string name) =>
        _blobs.TryGetValue(name, out var b)
            ? Task.FromResult<Stream>(new MemoryStream(b, writable: false))
            : throw new FileNotFoundException("blob not found");

    /// <summary>Lets a test make selected prefix deletions fail.</summary>
    public Func<string, bool>? FailDeletes { get; set; }

    public Task DeletePrefixAsync(string prefix)
    {
        if (FailDeletes?.Invoke(prefix) == true) throw new IOException("delete failed");
        foreach (var k in _blobs.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList()) _blobs.TryRemove(k, out _);
        return Task.CompletedTask;
    }

    public Uri PublicUrl(string name) =>
        (name.StartsWith(BlobNames.Originals + "/", StringComparison.Ordinal) || name.StartsWith(BlobNames.Exports + "/", StringComparison.Ordinal))
            ? throw new ArgumentException("Originals are private.", nameof(name))
            : new($"https://cdn.test/{name}");
}
