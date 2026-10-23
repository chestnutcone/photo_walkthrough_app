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

    public async Task InvokeAsync(HttpContext ctx)
    {
        try { await next(ctx); }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested) { throw; }
        catch (BadHttpRequestException) { throw; }
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
