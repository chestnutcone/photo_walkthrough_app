using Microsoft.AspNetCore.WebUtilities;
using System.Security.Cryptography;
using Vela.Api.Images;

namespace Vela.Api.Admin;

/// <summary>
/// Wraps every <c>/admin</c> response: 404 when the admin area is off, otherwise no-store plus the security headers and a
/// per-request style nonce. The pages carry no script at all.
/// </summary>
public sealed class AdminHeadersMiddleware(RequestDelegate next, AdminSettings settings, IAdminAllowList allow, IBlobStore blobs)
{
    public const string NonceKey = "vela.admin.nonce";

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!settings.Enabled || !allow.Any)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        var nonce = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(18));
        ctx.Items[NonceKey] = nonce;
        var origin = blobs.ReadOrigin.GetLeftPart(UriPartial.Authority);
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            h.CacheControl = "no-store";
            h.ContentSecurityPolicy =
                $"default-src 'none'; img-src 'self' {origin}; style-src 'nonce-{nonce}'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
            h.XContentTypeOptions = "nosniff";
            h["Referrer-Policy"] = "no-referrer";
            h.XFrameOptions = "DENY";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            return Task.CompletedTask;
        });
        await next(ctx);
    }
}
