using Vela.Api.Images;

namespace Vela.Api.Tests.Support;

/// <summary>Gates for tests that construct a generator directly (the app always injects the one process-wide singleton).</summary>
public static class TestGates
{
    /// <summary>The production default limit with a long wait, so unit tests never see 503 busy by accident.</summary>
    public static ImageDecodeGate Roomy() => new(ImageDecodeGate.DefaultMaxConcurrentDecodes, TimeSpan.FromMinutes(2));
}
