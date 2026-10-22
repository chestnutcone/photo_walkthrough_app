using Vela.Api.Branding;

namespace Vela.Api.Share;

/// <summary>Where the web story viewer lives. Share pages link here; validated at startup.</summary>
public sealed record WebOptions(string ViewerUrl)
{
    public string StoryUrl(Guid workId) => $"{ViewerUrl}/s/{workId}";
}

/// <summary>Universal-link / app-link identifiers for the well-known files. Empty values mean "not configured".</summary>
public sealed record LinksOptions(string AppleTeamId, string BundleId, string AndroidPackage, IReadOnlyList<string> Sha256Fingerprints);

/// <summary>DMCA designated agent contact. Shown only when configured.</summary>
public sealed record LegalOptions(string DmcaAgentName, string DmcaAgentEmail, string DmcaAgentAddress)
{
    public bool HasAgent => DmcaAgentName.Length > 0 && DmcaAgentEmail.Length > 0 && DmcaAgentAddress.Length > 0;
}

public static class ShareServiceCollectionExtensions
{
    /// <summary>Loads and validates share, link and legal settings. Outside Development and Test the viewer URL and branding domain must be https.</summary>
    public static IServiceCollection AddShare(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var lenient = env.EnvironmentName is "Development" or "Test";
        var branding = config.GetSection("Branding").Get<BrandingOptions>() ?? new BrandingOptions();
        if (!lenient) RequireHost(branding.Domain);

        services.AddSingleton(new WebOptions(LoadViewerUrl(config["Web:ViewerUrl"], branding.Domain, lenient)));
        services.AddSingleton(new LinksOptions(
            Value(config, "Links:AppleTeamId"), Value(config, "Links:BundleId"), Value(config, "Links:AndroidPackage"), Fingerprints(config)));
        services.AddSingleton(new LegalOptions(
            Value(config, "Legal:DmcaAgentName"), Value(config, "Legal:DmcaAgentEmail"), Value(config, "Legal:DmcaAgentAddress")));
        return services;
    }

    private static string Value(IConfiguration c, string key) => c[key]?.Trim() ?? "";

    /// <summary>An array (<c>Links:Sha256Fingerprints:0</c>) or one comma-separated value (environment variables).</summary>
    private static List<string> Fingerprints(IConfiguration c)
    {
        var section = c.GetSection("Links:Sha256Fingerprints");
        var children = section.GetChildren().Select(x => x.Value ?? "").ToList();
        var items = children.Count > 0 ? children : (section.Value ?? "").Split(',').ToList();
        return items.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToList();
    }

    private static string LoadViewerUrl(string? raw, string domain, bool lenient)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return lenient ? $"https://{domain}" : throw Invalid("is not configured. Set it via environment (Web__ViewerUrl) or a secret store");
        var ok = Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri) && uri.Host.Length > 0 && uri.UserInfo.Length == 0
            && (uri.Scheme == Uri.UriSchemeHttps || (lenient && uri.Scheme == Uri.UriSchemeHttp));
        if (!ok) throw Invalid("must be an absolute https URL");
        return uri!.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static void RequireHost(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain) || Uri.CheckHostName(domain) != UriHostNameType.Dns)
            throw new InvalidOperationException("Branding:Domain must be a bare host name (no scheme, path or port) outside Development and Test.");
    }

    private static InvalidOperationException Invalid(string why) => new($"Web:ViewerUrl {why}.");
}
