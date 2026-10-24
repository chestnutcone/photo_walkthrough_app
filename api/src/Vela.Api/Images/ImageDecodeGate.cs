namespace Vela.Api.Images;

/// <summary>
/// Process-wide limit on concurrent image decodes (S1b-1). A decode can need up to <see cref="VariantGenerator.MaxDecodeBytes"/>
/// of native memory, so without a cap a handful of parallel requests exhausts the container. A caller that cannot get a slot
/// within the configured wait gets <see cref="ImageBusyException"/> (503 busy with Retry-After): requests never queue unboundedly.
/// </summary>
public sealed class ImageDecodeGate : IDisposable
{
    public const string Section = "Images";
    public const int DefaultMaxConcurrentDecodes = 2;
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(5);
    /// <summary>What a busy caller is told to wait before retrying.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _slots;
    private readonly TimeSpan _wait;
    private int _inUse;
    private int _peak;

    public ImageDecodeGate(int maxConcurrentDecodes, TimeSpan wait)
    {
        if (maxConcurrentDecodes < 1) throw new ArgumentOutOfRangeException(nameof(maxConcurrentDecodes), "Images:MaxConcurrentDecodes must be at least 1.");
        if (wait < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(wait), "Images:DecodeWaitSeconds must not be negative.");
        Limit = maxConcurrentDecodes;
        _wait = wait;
        _slots = new SemaphoreSlim(maxConcurrentDecodes, maxConcurrentDecodes);
    }

    /// <summary>Reads <c>Images:MaxConcurrentDecodes</c> (default 2) and <c>Images:DecodeWaitSeconds</c> (default 5).</summary>
    public static ImageDecodeGate FromConfiguration(IConfiguration config)
    {
        var limit = config.GetValue<int?>($"{Section}:MaxConcurrentDecodes") ?? DefaultMaxConcurrentDecodes;
        var wait = config.GetValue<double?>($"{Section}:DecodeWaitSeconds") is { } s ? TimeSpan.FromSeconds(s) : DefaultWait;
        return new ImageDecodeGate(limit, wait);
    }

    public int Limit { get; }

    /// <summary>The most decodes ever held at once (for tests and diagnostics).</summary>
    public int Peak => Volatile.Read(ref _peak);

    /// <summary>Waits at most the configured time for a slot; dispose the result to release it.</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken ct = default)
    {
        if (!await _slots.WaitAsync(_wait, ct)) throw new ImageBusyException(RetryAfter);
        var now = Interlocked.Increment(ref _inUse);
        int seen;
        while (now > (seen = Volatile.Read(ref _peak)) && Interlocked.CompareExchange(ref _peak, now, seen) != seen) { }
        return new Slot(this);
    }

    private void Release()
    {
        Interlocked.Decrement(ref _inUse);
        _slots.Release();
    }

    public void Dispose() => _slots.Dispose();

    private sealed class Slot(ImageDecodeGate gate) : IDisposable
    {
        private int _released;
        public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release(); }
    }
}

/// <summary>No decode slot became free in time. Mapped to 503 <c>busy</c> with Retry-After; the upload is left untouched.</summary>
public sealed class ImageBusyException(TimeSpan retryAfter) : Exception("busy")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
