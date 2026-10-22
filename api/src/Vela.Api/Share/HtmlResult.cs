using System.Security.Cryptography;
using System.Text;

namespace Vela.Api.Share;

/// <summary>An HTML response with the standard security headers and a per-request style nonce. Pages contain no script.</summary>
public sealed class HtmlResult(int status, string cacheControl, Func<string, string> render) : IResult
{
    public static IResult Found(Func<string, string> render) => new HtmlResult(StatusCodes.Status200OK, "public, max-age=300", render);

    public static IResult NotFound(string message) =>
        new HtmlResult(StatusCodes.Status404NotFound, "no-store", nonce => HtmlLayout.Message(nonce, message));

    public static string NewNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));

    public async Task ExecuteAsync(HttpContext ctx)
    {
        var nonce = NewNonce();
        var bytes = Encoding.UTF8.GetBytes(render(nonce));
        var h = ctx.Response.Headers;
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        h.ContentSecurityPolicy =
            $"default-src 'none'; img-src https:; style-src 'nonce-{nonce}'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        h.XContentTypeOptions = "nosniff";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h.XFrameOptions = "DENY";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        h.CacheControl = cacheControl;
        ctx.Response.ContentLength = bytes.Length;
        if (HttpMethods.IsHead(ctx.Request.Method)) return;
        await ctx.Response.Body.WriteAsync(bytes);
    }
}
