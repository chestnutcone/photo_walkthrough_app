using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Sentry;

namespace Vela.Api.Telemetry;

/// <summary>
/// Turns unexpected exceptions into the standard 500 envelope. The body never carries exception text or a stack trace,
/// in any environment. Client aborts and Kestrel's own bad-request errors keep their default handling.
/// </summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> log)
{
    public const string TraceHeader = "X-Trace-Id";
    public const string Message = "Something went wrong. Please try again.";
    public const string BusyMessage = "We're busy processing images. Try again in a few seconds.";

    public async Task InvokeAsync(HttpContext ctx)
    {
        try { await next(ctx); }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested) { throw; }
        catch (BadHttpRequestException) { throw; }
        catch (Vela.Api.Images.ImageBusyException e) when (!ctx.Response.HasStarted)
        {
            // Expected back-pressure from the decode gate, not a fault: no error log, no Sentry event.
            ctx.Response.Clear();
            ctx.Response.Headers.RetryAfter = ((int)Math.Ceiling(e.RetryAfter.TotalSeconds)).ToString();
            await Vela.Api.Common.Results.Fail(StatusCodes.Status503ServiceUnavailable, "busy", BusyMessage).ExecuteAsync(ctx);
        }
        catch (Exception e)
        {
            var traceId = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;
            // Type name only: exception messages can echo user input, so they stay out of the logs.
            log.LogError("Unhandled exception {ExceptionType} on {Method} (trace {TraceId}).", e.GetType().Name, ctx.Request.Method, traceId);
            Activity.Current?.SetStatus(ActivityStatusCode.Error);
            SentrySdk.CaptureException(e);

            if (ctx.Response.HasStarted) { ctx.Abort(); return; }
            ctx.Response.Clear();
            ctx.Response.Headers[TraceHeader] = traceId;
            await Vela.Api.Common.Results.Fail(StatusCodes.Status500InternalServerError, "internal_error", Message).ExecuteAsync(ctx);
        }
    }
}
