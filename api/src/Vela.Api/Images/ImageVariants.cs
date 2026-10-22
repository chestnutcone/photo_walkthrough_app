namespace Vela.Api.Images;

/// <summary>Result of variant generation. Width/Height are those of the orientation-corrected original.</summary>
public sealed record ImageVariants(int Width, int Height, string Thumb, string Feed, string Full);

/// <summary>An upload that cannot be used. Carries a reason code only (never user text or file content).</summary>
public sealed class InvalidImageException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}
