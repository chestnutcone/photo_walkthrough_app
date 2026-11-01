using System.Text.Json;

namespace Vela.Api.Share;

/// <summary>Universal-link (iOS) and app-link (Android) association files, generated from config. Missing config is a 404, never a placeholder.</summary>
public static class WellKnownEndpoints
{
    public static IEndpointRouteBuilder MapWellKnown(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/apple-app-site-association", (LinksOptions links) =>
            links.AppleTeamId.Length == 0 || links.BundleId.Length == 0
                ? Missing()
                : Json(new
                {
                    applinks = new
                    {
                        details = new[]
                        {
                            new { appIDs = new[] { $"{links.AppleTeamId}.{links.BundleId}" }, components = new[] { new Dictionary<string, string> { ["/"] = "/s/*" } } },
                        },
                    },
                }));
        app.MapGet("/.well-known/assetlinks.json", (LinksOptions links) =>
            links.AndroidPackage.Length == 0 || links.Sha256Fingerprints.Count == 0
                ? Missing()
                : Json(new[]
                {
                    new
                    {
                        relation = new[] { "delegate_permission/common.handle_all_urls" },
                        target = new { @namespace = "android_app", package_name = links.AndroidPackage, sha256_cert_fingerprints = links.Sha256Fingerprints },
                    },
                }));
        return app;
    }

    private static IResult Missing() => Microsoft.AspNetCore.Http.Results.NotFound();

    private static IResult Json(object value) =>
        Microsoft.AspNetCore.Http.Results.Text(JsonSerializer.Serialize(value), "application/json");
}
