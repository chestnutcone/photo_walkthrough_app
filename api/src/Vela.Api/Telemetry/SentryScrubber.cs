using Microsoft.Extensions.Configuration;
using Sentry;
using Sentry.AspNetCore;
using Sentry.Extensibility;

namespace Vela.Api.Telemetry;

/// <summary>Keeps Sentry reports free of email addresses, IPs, request bodies, cookies and credentials.</summary>
public static class SentryScrubber
{
    private static readonly HashSet<string> DroppedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cookie", "Set-Cookie", "Proxy-Authorization", "X-Forwarded-For", "X-Real-IP", "CF-Connecting-IP", "True-Client-IP",
        "X-Vela-Attestation", "X-Api-Key",
    };

    public static void Configure(SentryAspNetCoreOptions o, IConfiguration config, string environment)
    {
        var t = TelemetryOptions.Load(config);
        o.Dsn = t.SentryDsn;
        o.Environment = environment;
        o.SendDefaultPii = false;
        o.MaxRequestBodySize = RequestSize.None;
        o.TracesSampleRate = t.SamplingRatio;
        o.AttachStacktrace = true;
        o.SetBeforeSend((evt, _) => Apply(evt));
        o.SetBeforeSendTransaction((t, _) => ApplyTransaction(t));
        o.SetBeforeBreadcrumb((crumb, _) => ScrubBreadcrumb(crumb));
    }

    public static SentryEvent? Apply(SentryEvent evt)
    {
        ScrubCommon(evt);
        evt.ServerName = null;

        if (evt.Message is { } m)
        {
            m.Message = PiiScrubber.Scrub(m.Message);
            m.Formatted = PiiScrubber.Scrub(m.Formatted);
        }
        foreach (var x in evt.SentryExceptions ?? []) x.Value = PiiScrubber.Scrub(x.Value);
        // Extra and contexts are free-form; drop them rather than guess which values are personal.
        foreach (var key in evt.Extra.Keys.ToList()) evt.SetExtra(key, SensitiveMask);
        ScrubBreadcrumbs(evt);
        return evt;
    }

    public static SentryTransaction? ApplyTransaction(SentryTransaction t)
    {
        ScrubCommon(t);
        t.Description = PiiScrubber.Scrub(t.Description);
        foreach (var span in t.Spans) span.Description = PiiScrubber.Scrub(span.Description);
        foreach (var key in t.Extra.Keys.ToList()) t.SetExtra(key, SensitiveMask);
        return t;
    }

    /// <summary>User, request and tags: the parts events and transactions share.</summary>
    private static void ScrubCommon(IEventLike e)
    {
        e.User.Email = null;
        e.User.IpAddress = null;
        e.User.Username = null;
        foreach (var key in e.Tags.Keys.ToList()) e.SetTag(key, PiiScrubber.Scrub(e.Tags[key])!);

        var req = e.Request;
        req.Data = null;
        req.Cookies = null;
        req.QueryString = PiiScrubber.Scrub(req.QueryString);
        req.Url = PiiScrubber.Scrub(req.Url);
        foreach (var key in req.Headers.Keys.ToList())
        {
            if (DroppedHeaders.Contains(key)) req.Headers.Remove(key);
            else req.Headers[key] = PiiScrubber.Scrub(req.Headers[key])!;
        }
        foreach (var key in req.Env.Keys.ToList())
            if (key.Contains("ADDR", StringComparison.OrdinalIgnoreCase) || key.Contains("HOST", StringComparison.OrdinalIgnoreCase)) req.Env.Remove(key);
            else req.Env[key] = PiiScrubber.Scrub(req.Env[key])!;
    }

    private static void ScrubBreadcrumbs(SentryEvent evt)
    {
        var scrubbed = evt.Breadcrumbs.Select(b => ScrubBreadcrumb(b)!).ToList();
        // The SDK keeps breadcrumbs in a mutable list. If that ever changes we cannot replace them, so fail closed:
        // BeforeBreadcrumb already scrubbed each one on insert, and an unexpected collection type is an error to surface.
        if (evt.Breadcrumbs is not ICollection<Breadcrumb> { IsReadOnly: false } list)
            throw new InvalidOperationException("Sentry breadcrumbs are no longer a mutable collection; update SentryScrubber.");
        list.Clear();
        foreach (var b in scrubbed) list.Add(b);
    }

    private const string SensitiveMask = "[removed]";

    internal static Breadcrumb? ScrubBreadcrumb(Breadcrumb crumb) =>
        new(PiiScrubber.Scrub(crumb.Message) ?? "", crumb.Type ?? "default", null, crumb.Category, crumb.Level);
}
