namespace Vela.Api.Moderation;

/// <summary>Development/Test scanner: text containing <see cref="UnsafeMarker"/> scores 6; images are controlled by the test.</summary>
public sealed class FakeContentSafety : IContentSafety
{
    public const string UnsafeMarker = "[[unsafe]]";
    private int _imageScans;

    /// <summary>Severity returned for every image unless <see cref="ImageRule"/> is set.</summary>
    public int ImageSeverity { get; set; }
    public Func<byte[], int>? ImageRule { get; set; }
    /// <summary>When true every call throws, simulating an outage.</summary>
    public bool Throw { get; set; }
    public int ImageScans => _imageScans;
    public List<string> TextRequests { get; } = [];

    public Task<int> SeverityAsync(string text)
    {
        if (Throw) throw new InvalidOperationException("Content Safety unavailable (fake).");
        lock (TextRequests) TextRequests.Add(text);
        return Task.FromResult(text.Contains(UnsafeMarker, StringComparison.Ordinal) ? 6 : 0);
    }

    public async Task<int> SeverityAsync(Stream image)
    {
        if (Throw) throw new InvalidOperationException("Content Safety unavailable (fake).");
        Interlocked.Increment(ref _imageScans);
        if (ImageRule is null) return ImageSeverity;
        using var ms = new MemoryStream();
        await image.CopyToAsync(ms);
        return ImageRule(ms.ToArray());
    }
}
