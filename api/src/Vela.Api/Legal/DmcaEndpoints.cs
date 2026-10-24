using Vela.Api.Abuse;
using System.Text.Json;
using Vela.Api.Auth;
using Vela.Api.Common;
using Vela.Api.Social;

namespace Vela.Api.Legal;

public sealed record DmcaAck(Guid NoticeId);

public static class DmcaEndpoints
{
    public static IServiceCollection AddDmca(this IServiceCollection services) => services.AddScoped<IDmcaService, DmcaService>().AddScoped<ICounterNoticeService, CounterNoticeService>();

    public static IEndpointRouteBuilder MapDmcaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/legal/dmca-notices", async (HttpContext ctx, IDmcaService svc, IClientIpResolver ips) =>
        {
            try
            {
                var body = await ReadBodyAsync(ctx);
                if (body is null) return Common.Results.Fail(StatusCodes.Status413PayloadTooLarge, "payload_too_large", "The notice is too large.");
                var request = DmcaValidator.Parse(Parse(body));
                // Always 202, whether or not the reference matched a story: this endpoint must not reveal which stories exist.
                return Common.Results.Accepted(new DmcaAck(await svc.SubmitAsync(request, ips.Resolve(ctx))));
            }
            catch (SafetyException e)
            {
                if (e.RetryAfter is { } wait) ctx.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
                return Common.Results.Fail(e.Status, e.Code, e.Message);
            }
        }).AllowAnonymous().RequirePublicFormLimit().WithName("submitDmcaNotice"); // S1b-5: on top of its own 5 per address per day

        app.MapPost("/v1/works/{id}/counter-notice", async (string id, HttpContext ctx, ICurrentUser me, ICounterNoticeService svc) =>
        {
            try
            {
                var body = await ReadBodyAsync(ctx);
                if (body is null) return Common.Results.Fail(StatusCodes.Status413PayloadTooLarge, "payload_too_large", "The counter-notice is too large.");
                var request = CounterNoticeService.Parse(Parse(body));
                var workId = Guid.TryParse(id, out var g) ? g : throw CounterNoticeService.NotFound();
                return Common.Results.Ok(new DmcaAck(await svc.SubmitAsync(me.UserId!.Value, workId, request)));
            }
            catch (SafetyException e)
            {
                return Common.Results.Fail(e.Status, e.Code, e.Message);
            }
        }).RequireAuthorization().WithName("submitCounterNotice");
        return app;
    }

    private static JsonElement Parse(byte[] body)
    {
        try { return JsonDocument.Parse(body).RootElement.Clone(); }
        catch (JsonException) { throw DmcaValidator.Invalid("body", "must be valid JSON"); }
    }

    /// <summary>Reads at most the size cap; null when the body is larger.</summary>
    private static async Task<byte[]?> ReadBodyAsync(HttpContext ctx)
    {
        if (ctx.Request.ContentLength > DmcaValidator.MaxBodyBytes) return null;
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await ctx.Request.Body.ReadAsync(buffer)) > 0)
        {
            if (ms.Length + read > DmcaValidator.MaxBodyBytes) return null;
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }
}
