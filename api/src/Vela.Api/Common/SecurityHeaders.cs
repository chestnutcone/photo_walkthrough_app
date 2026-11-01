namespace Vela.Api.Common;

/// <summary>
/// Headers every response carries, whatever produced it (endpoint, error handler, rate limiter, static 404): MIME sniffing is
/// off, and outside Development and Test browsers are told to use https only. Page-specific headers (CSP, framing, referrer
/// policy) are added by <see cref="Share.HtmlResult"/> and the admin middleware.
/// </summary>
public static class SecurityHeaders
{
    public const string HstsSetting = "Security:Hsts";
    public const string HstsValue = "max-age=31536000; includeSubDomains";

    /// <summary>On unless explicitly disabled, except in Development and Test (plain http on localhost). "preload" is deliberately not sent: it is a long-lived, hard-to-undo commitment for the owner to make.</summary>
    public static bool HstsEnabled(string? configured, string environmentName) =>
        bool.TryParse(configured, out var explicitValue) ? explicitValue : environmentName is not ("Development" or "Test");

    /// <summary>
    /// S1b-13: API answers that are personal (any request carrying a bearer token) or that are not plain reads (token pairs,
    /// writes) are <c>no-store</c>, so even a "cache everything" CDN rule cannot hand one user's data to another. Anonymous
    /// public reads keep their (absent) caching headers. Every API answer varies by Authorization, so a cache never serves an
    /// anonymous copy to a signed-in caller. Share pages and /admin set their own headers and are not under /v1.
    /// </summary>
    internal static void ApplyApiCaching(HttpContext ctx)
    {
        if (!ctx.Request.Path.StartsWithSegments("/v1")) return;
        var h = ctx.Response.Headers;
        h.Append("Vary", "Authorization");
        var anonymousRead = (HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method))
            && !ctx.Request.Headers.ContainsKey("Authorization");
        if (!anonymousRead) h.CacheControl = "no-store";
    }

    /// <summary>Call first, so error responses are covered too.</summary>
    public static IApplicationBuilder UseVelaSecurityHeaders(this IApplicationBuilder app, IConfiguration config, IHostEnvironment env)
    {
        var hsts = HstsEnabled(config[HstsSetting], env.EnvironmentName);
        return app.Use((ctx, next) =>
        {
            ctx.Response.OnStarting(() =>
            {
                var h = ctx.Response.Headers;
                h.XContentTypeOptions = "nosniff";
                if (hsts) h.StrictTransportSecurity = HstsValue;
                ApplyApiCaching(ctx);
                return Task.CompletedTask;
            });
            return next(ctx);
        });
    }
}
