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
                return Task.CompletedTask;
            });
            return next(ctx);
        });
    }
}
