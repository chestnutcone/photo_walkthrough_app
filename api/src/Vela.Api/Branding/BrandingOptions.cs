namespace Vela.Api.Branding;

/// <summary>Single source for user-facing names, bundle ids and domains. "Vela" is a code name.</summary>
public sealed class BrandingOptions
{
    public string AppName { get; init; } = "Vela";
    public string BundleId { get; init; } = "dev.vela.app";
    public string Domain { get; init; } = "vela.example";
}
