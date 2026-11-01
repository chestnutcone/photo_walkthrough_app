using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Vela.Api.Account;

/// <summary>Cloudflare zone and API token (needs only the "Cache Purge" permission) for the image CDN and share pages.</summary>
public sealed record CloudflareOptions(string ZoneId, string ApiToken, string ShareBaseUrl, string ImageHost = "")
{
    /// <summary>The CDN host behind Storage:PublicBaseUrl, or empty when images are served straight from blob storage or locally (not on the zone, so nothing to purge).</summary>
    internal static string CdnHost(string? publicBaseUrl)
    {
        if (!Uri.TryCreate(publicBaseUrl?.Trim(), UriKind.Absolute, out var uri) || uri.IsLoopback) return "";
        return uri.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase) ? "" : uri.Host;
    }

    /// <summary>Null when neither key is set (no CDN purge); throws when only one is.</summary>
    public static CloudflareOptions? Load(IConfiguration config)
    {
        var zone = config["Cloudflare:ZoneId"]?.Trim() ?? "";
        var token = config["Cloudflare:ApiToken"]?.Trim() ?? "";
        if (zone.Length == 0 && token.Length == 0) return null;
        if (zone.Length == 0) throw new InvalidOperationException("Cloudflare:ZoneId is not configured but Cloudflare:ApiToken is. Set both, or neither.");
        if (token.Length == 0) throw new InvalidOperationException("Cloudflare:ApiToken is not configured but Cloudflare:ZoneId is. Set it via environment (Cloudflare__ApiToken) or a secret store.");
        var domain = config["Branding:Domain"]?.Trim() ?? "";
        return new CloudflareOptions(zone, token, domain.Length > 0 ? $"https://{domain}" : "", CdnHost(config["Storage:PublicBaseUrl"]));
    }
}

/// <summary>
/// Purges Cloudflare's cache through the purge_cache API. Entries ending in "/" are purged as prefixes (available on every
/// plan), other absolute URLs as files, and "/path" entries (share pages) against the share host. Any failure throws so the
/// caller (take-down reconciler, deletion job) keeps its work item and retries; messages never include URLs or the token.
/// </summary>
public sealed class CloudflareCdnPurger(HttpClient http, CloudflareOptions options, ILogger<CloudflareCdnPurger> log) : ICdnPurger
{
    public const int BatchSize = 30;
    private readonly Uri _endpoint = new($"https://api.cloudflare.com/client/v4/zones/{Uri.EscapeDataString(options.ZoneId)}/purge_cache");

    public async Task PurgeAsync(IReadOnlyList<string> publicUrlsOrPaths)
    {
        var files = new List<string>();
        var prefixes = new List<string>();
        var skipped = 0;
        foreach (var raw in publicUrlsOrPaths.Distinct())
        {
            var entry = Resolve(raw);
            if (!InZone(entry)) { skipped++; continue; }
            if (entry.EndsWith('/') && entry.Split("://", 2) is [_, var noScheme]) prefixes.Add(noScheme);
            else files.Add(entry);
        }
        // Cloudflare rejects URLs outside the zone (and a permanent failure would block deletions forever), so they are skipped.
        if (skipped > 0) log.LogWarning("CDN purge skipped {Count} entries outside the Cloudflare zone (is Storage:PublicBaseUrl on the zone?).", skipped);
        foreach (var batch in files.Chunk(BatchSize)) await SendAsync(new { files = batch }, batch.Length);
        foreach (var batch in prefixes.Chunk(BatchSize)) await SendPrefixesAsync(batch);
    }

    private static readonly string[] WorkVariants = ["thumb.webp", "feed.webp", "full.webp"];
    private static readonly string[] AvatarVariants = ["avatar-256.webp"];

    /// <summary>Prefix purge is not available on every plan or token; on a 4xx the known variant files are purged instead. 5xx and 429 still throw for retry.</summary>
    private async Task SendPrefixesAsync(string[] batch)
    {
        try { await SendAsync(new { prefixes = batch }, batch.Length); }
        catch (HttpRequestException e) when (e.StatusCode is { } code && (int)code is >= 400 and < 500 && code != HttpStatusCode.TooManyRequests)
        {
            log.LogWarning("Cloudflare prefix purge rejected (HTTP {Status}); purging known variant files instead.", (int)code);
            var files = batch.SelectMany(VariantFiles).ToArray();
            foreach (var chunk in files.Chunk(BatchSize)) await SendAsync(new { files = chunk }, chunk.Length);
        }
    }

    private static IEnumerable<string> VariantFiles(string prefixNoScheme)
    {
        var names = prefixNoScheme.Contains("/avatars/") ? AvatarVariants : prefixNoScheme.Contains("/works/") ? WorkVariants : [];
        return names.Select(n => $"https://{prefixNoScheme}{n}");
    }

    private bool InZone(string entry)
    {
        if (!Uri.TryCreate(entry, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host;
        var share = Uri.TryCreate(options.ShareBaseUrl, UriKind.Absolute, out var s) ? s.Host : "";
        return (options.ImageHost.Length > 0 && string.Equals(host, options.ImageHost, StringComparison.OrdinalIgnoreCase))
            || (share.Length > 0 && string.Equals(host, share, StringComparison.OrdinalIgnoreCase));
    }

    private string Resolve(string entry)
    {
        if (!entry.StartsWith('/')) return entry;
        if (options.ShareBaseUrl.Length == 0) throw new InvalidOperationException("Branding:Domain is needed to purge share pages.");
        return options.ShareBaseUrl + entry;
    }

    private async Task SendAsync<T>(T body, int count)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = JsonContent.Create(body) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiToken);
        using var res = await http.SendAsync(req);
        var ok = res.IsSuccessStatusCode && await ReportsSuccess(res);
        if (!ok)
        {
            log.LogError("Cloudflare purge of {Count} entries failed (HTTP {Status}).", count, (int)res.StatusCode);
            throw new HttpRequestException($"Cloudflare purge_cache failed with HTTP {(int)res.StatusCode}.", null, res.StatusCode);
        }
    }

    private static async Task<bool> ReportsSuccess(HttpResponseMessage res)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
            return doc.RootElement.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { return false; }
    }
}

public static class CdnPurgingExtensions
{
    /// <summary>Replaces the no-op purger with the Cloudflare one when Cloudflare:ZoneId and Cloudflare:ApiToken are set.</summary>
    public static IServiceCollection AddCdnPurging(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        if (CloudflareOptions.Load(config) is not { } options) return services;
        services.AddSingleton(options);
        services.AddHttpClient<CloudflareCdnPurger>(c => c.Timeout = TimeSpan.FromSeconds(15));
        services.Replace(ServiceDescriptor.Transient<ICdnPurger>(sp => sp.GetRequiredService<CloudflareCdnPurger>()));
        return services;
    }

    /// <summary>Outside Development and Test, a missing purger means deleted and taken-down images stay cached at the edge.</summary>
    public static void WarnIfNotConfigured(ICdnPurger purger, IHostEnvironment env, ILogger log)
    {
        if (env.IsDevelopment() || env.IsEnvironment("Test") || purger is not NoOpCdnPurger) return;
        log.LogWarning("CDN purging is NOT configured (Cloudflare:ZoneId / Cloudflare:ApiToken missing): deleted and taken-down images stay cached at the edge until they expire.");
    }
}
