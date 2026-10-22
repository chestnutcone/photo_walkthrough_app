using System.Text.Json;
using Vela.Api.Auth;
using Vela.Api.Common;
using Vela.Api.Social;

namespace Vela.Api.Inquiries;

public static class InquiryEndpoints
{
    public static IServiceCollection AddInquiries(this IServiceCollection services) =>
        services.AddScoped<IInquiryService, InquiryService>();

    public static IEndpointRouteBuilder MapInquiryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/artists/{id}/inquiries", async (string id, HttpContext ctx, ICurrentUser me, IInquiryService svc, IClientIpResolver ips) =>
        {
            try
            {
                var artist = Guid.TryParse(id, out var a) ? a : throw InquiryService.ArtistNotFound();
                var req = await ReadAsync(ctx);
                return Common.Results.Ok(await svc.SendAsync(me.UserId!.Value, artist, req, ips.Resolve(ctx)));
            }
            catch (SafetyException e)
            {
                if (e.RetryAfter is { } wait) ctx.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
                return Common.Results.Fail(e.Status, e.Code, e.Message);
            }
        }).RequireAuthorization().WithName("sendInquiry");
        return app;
    }

    private static async Task<InquiryRequest> ReadAsync(HttpContext ctx)
    {
        JsonElement root;
        try
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
            root = doc.RootElement.Clone();
        }
        catch (JsonException) { throw InquiryValidator.Invalid("body must be valid JSON."); }
        if (root.ValueKind != JsonValueKind.Object) throw InquiryValidator.Invalid("body must be a JSON object.");
        return new InquiryRequest(Text(root, "type"), Text(root, "email"), Text(root, "message"), OptionalGuid(root, "workId"));
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : throw InquiryValidator.Invalid($"{name} must be text.");

    private static Guid? OptionalGuid(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        return v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var id) ? id : throw InquiryValidator.Invalid($"{name} must be an id.");
    }
}
