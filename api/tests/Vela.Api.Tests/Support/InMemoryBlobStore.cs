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

    public Uri ReadOrigin => new("https://blob.test/");

    /// <summary>Every read URL handed out, so tests can check lifetimes.</summary>
    public List<(string Name, int TtlSeconds)> ReadUrls { get; } = [];

    public Uri CreateReadUrl(string name, TimeSpan ttl)
    {
        var seconds = (int)Math.Min(ttl.TotalSeconds, 900);
        lock (ReadUrls) ReadUrls.Add((name, seconds));
        return new($"https://blob.test/{name}?sp=r&ttl={seconds}&sig=SECRETSIG");
    }

    /// <summary>Awaited before every read, so a test can hold a read open (for example to observe locks or concurrency).</summary>
    public Func<string, Task>? BeforeRead { get; set; }

    public async Task<Stream> OpenReadAsync(string name)
    {
        if (BeforeRead is { } hook) await hook(name);
        return _blobs.TryGetValue(name, out var b)
            ? new MemoryStream(b, writable: false)
            : throw new FileNotFoundException("blob not found");
    }

    /// <summary>Lets a test make selected prefix deletions fail.</summary>
    public Func<string, bool>? FailDeletes { get; set; }

    public Task DeletePrefixAsync(string prefix)
    {
        if (FailDeletes?.Invoke(prefix) == true) throw new IOException("delete failed");
        foreach (var k in _blobs.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList()) _blobs.TryRemove(k, out _);
        return Task.CompletedTask;
    }

    public Uri PublicUrl(string name) =>
        (name.StartsWith(BlobNames.Originals + "/", StringComparison.Ordinal) || name.StartsWith(BlobNames.Exports + "/", StringComparison.Ordinal) || name.StartsWith(BlobNames.Evidence + "/", StringComparison.Ordinal))
            ? throw new ArgumentException("Originals are private.", nameof(name))
            : new($"https://cdn.test/{name}");
}
