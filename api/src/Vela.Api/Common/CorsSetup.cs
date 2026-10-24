using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;
using Vela.Api.Share;

namespace Vela.Api.Common;

/// <summary>
/// Cross-origin reads for the web story viewer, which is served from its own origin (<c>Web:ViewerUrl</c>) and calls the public
/// read endpoints. Exactly that one origin is allowed (no wildcard), for reads only, and never with credentials: the viewer
/// has no sign-in. In Development a localhost origin is also allowed so <c>flutter run -d chrome</c> works. Admin pages get no
/// CORS headers at all.
/// </summary>
public static class CorsSetup
{
    public const string PolicyName = "web-viewer";

    public static IServiceCollection AddViewerCors(this IServiceCollection services)
    {
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<WebOptions, IHostEnvironment>((options, web, env) =>
        {
            var viewerOrigin = new Uri(web.ViewerUrl).GetLeftPart(UriPartial.Authority);
            options.AddPolicy(PolicyName, policy => policy
                .SetIsOriginAllowed(origin => IsAllowedOrigin(origin, viewerOrigin, env.IsDevelopment()))
                .WithMethods("GET", "HEAD", "OPTIONS")
                .WithHeaders("X-Platform", "Content-Type", "Accept")
                .WithExposedHeaders("Retry-After", "X-Trace-Id")
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10)));
        });
        return services;
    }

    /// <summary>Exact, case-insensitive origin match. "null" and look-alike hosts never match.</summary>
    public static bool IsAllowedOrigin(string origin, string viewerOrigin, bool development)
    {
        if (string.Equals(origin, viewerOrigin, StringComparison.OrdinalIgnoreCase)) return true;
        return development && Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" && uri.Host is "localhost" or "127.0.0.1";
    }

    /// <summary>Call before rate limiting so preflights are answered cheaply. Not applied to /admin.</summary>
    public static IApplicationBuilder UseViewerCors(this IApplicationBuilder app) =>
        app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/admin"), b => b.UseCors(PolicyName));
}
