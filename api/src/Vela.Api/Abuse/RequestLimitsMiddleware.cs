using System.Text.Json;
using Vela.Api.Common;

namespace Vela.Api.Abuse;

/// <summary>JSON bodies: at most 1 MB and 32 levels of nesting. The body is read once here and replayed to the endpoint.</summary>
public sealed class RequestLimitsMiddleware(RequestDelegate next)
{
    public const int MaxBodyBytes = 1024 * 1024;
    public const int MaxJsonDepth = 32;

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!ctx.Request.HasJsonContentType() || ctx.Request.ContentLength == 0)
        {
            await next(ctx);
            return;
        }
        if (ctx.Request.ContentLength > MaxBodyBytes)
        {
            await TooLarge().ExecuteAsync(ctx);
            return;
        }

        var body = await ReadBoundedAsync(ctx.Request.Body, ctx.RequestAborted);
        if (body is null)
        {
            await TooLarge().ExecuteAsync(ctx);
            return;
        }
        if (TooDeep(body))
        {
            await Results.Fail(StatusCodes.Status400BadRequest, "invalid_json", "The request body is nested too deeply.").ExecuteAsync(ctx);
            return;
        }
        ctx.Request.Body = new MemoryStream(body, writable: false);
        await next(ctx);
    }

    private static IResult TooLarge() =>
        Results.Fail(StatusCodes.Status413PayloadTooLarge, "payload_too_large", "The request body is too large.");

    /// <summary>Null when the stream holds more than the cap.</summary>
    private static async Task<byte[]?> ReadBoundedAsync(Stream source, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > MaxBodyBytes) return null;
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }

    /// <summary>Malformed JSON is not this middleware's business: the endpoint reports it in its own way.</summary>
    private static bool TooDeep(byte[] body)
    {
        var reader = new Utf8JsonReader(body, new JsonReaderOptions { MaxDepth = int.MaxValue });
        try
        {
            while (reader.Read())
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray && reader.CurrentDepth + 1 > MaxJsonDepth)
                    return true;
        }
        catch (JsonException) { }
        return false;
    }
}
