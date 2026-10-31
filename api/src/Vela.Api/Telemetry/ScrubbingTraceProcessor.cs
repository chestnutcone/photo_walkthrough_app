using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Vela.Api.Telemetry;

/// <summary>
/// Scrubs spans before export (FINALAPI I2). ASP.NET and HttpClient instrumentation record the raw path and query: search text
/// (<c>/v1/search?q=...</c>, which can be an email or a name), handles and user ids in paths, cursors and SAS signatures. The
/// path is replaced by its route template, the query and full URL are cut to scheme, host and path, and the client address is
/// dropped. Spans whose route is unknown (404s, static files) keep no path at all.
/// </summary>
public sealed class ScrubbingTraceProcessor : BaseProcessor<Activity>
{
    public const string Redacted = "[redacted]";

    // Current semantic-convention names and the older ones some instrumentations still emit.
    private static readonly string[] DroppedTags =
        ["url.query", "client.address", "client.port", "http.client_ip", "net.sock.peer.addr", "network.peer.address", "user.id", "enduser.id"];
    private static readonly string[] PathTags = ["url.path", "http.target"];
    private static readonly string[] FullUrlTags = ["url.full", "http.url"];

    public override void OnEnd(Activity activity)
    {
        foreach (var tag in DroppedTags) activity.SetTag(tag, null);

        var route = activity.GetTagItem("http.route") as string;
        foreach (var tag in PathTags)
            if (activity.GetTagItem(tag) is not null) activity.SetTag(tag, route ?? Redacted);

        foreach (var tag in FullUrlTags)
            if (activity.GetTagItem(tag) is string url) activity.SetTag(tag, WithoutQuery(url, route));

        // A display name is "GET /v1/artists/{handle}" (template) when a route matched; anything else is cut to the method.
        if (route is null && activity.Kind == ActivityKind.Server && activity.DisplayName.Contains('/'))
            activity.DisplayName = activity.DisplayName.Split(' ', 2)[0];
    }

    /// <summary>Scheme, host and path only; for a server span the path becomes the route template.</summary>
    internal static string WithoutQuery(string url, string? route)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return Redacted;
        var path = route is null ? u.AbsolutePath : "/" + route.TrimStart('/');
        return $"{u.Scheme}://{u.Authority}{(route is null && LooksPersonal(u.AbsolutePath) ? "/" + Redacted : path)}";
    }

    // Outbound calls (JWKS, Content Safety, Cloudflare, blob storage) have fixed or GUID-named paths; an email-looking or
    // otherwise unexpected path segment is dropped rather than exported.
    private static bool LooksPersonal(string path) => path.Contains('@') || path.Contains("%40", StringComparison.OrdinalIgnoreCase);
}

public static class TraceScrubbingExtensions
{
    /// <summary>Adds the scrubber; call it before any exporter so exporters only ever see scrubbed spans.</summary>
    public static OpenTelemetry.Trace.TracerProviderBuilder AddVelaTraceScrubbing(this OpenTelemetry.Trace.TracerProviderBuilder b) =>
        b.AddProcessor(new ScrubbingTraceProcessor());
}
