using System.Text.Json;
using Vela.Api.Auth;
using Vela.Api.Images;
using Vela.Api.Profiles;

namespace Vela.Api.Works;

public sealed record CreateDraftResult(Guid WorkId, string UploadUrl, DateTimeOffset ExpiresAt);

public static class WorkEndpoints
{
    public static readonly TimeSpan UploadUrlLifetime = TimeSpan.FromMinutes(15);
    private const int MaxTitleLength = 200;
    private static readonly string[] ContentTypes = ["image/jpeg", "image/png", "image/webp"];

    public static IServiceCollection AddWorks(this IServiceCollection services) =>
        services.AddScoped<IQuotaService, QuotaService>();

    public static IEndpointRouteBuilder MapWorkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/works", CreateDraftAsync).RequireAuthorization().WithName("createDraft");
        return app;
    }

    private static async Task<IResult> CreateDraftAsync(
        HttpContext ctx, ICurrentUser me, IQuotaService quota, IBlobStore blobs, TimeProvider clock)
    {
        try
        {
            var title = await ReadRequestAsync(ctx);
            var work = await quota.CreateDraftAsync(me.UserId!.Value, title);
            var url = blobs.CreateUploadUrl(work.ImageBlob, UploadUrlLifetime);
            return Results.Ok(new CreateDraftResult(work.Id, url.ToString(), clock.GetUtcNow() + UploadUrlLifetime));
        }
        catch (QuotaExceededException e)
        {
            return Results.Fail(StatusCodes.Status409Conflict, "quota_exceeded", e.Message);
        }
        catch (ProfileException e)
        {
            return Results.Fail(e.Status, e.Code, e.Message);
        }
    }

    private static async Task<string?> ReadRequestAsync(HttpContext ctx)
    {
        JsonElement body;
        try { body = await ctx.Request.ReadFromJsonAsync<JsonElement>(); }
        catch (JsonException) { throw ProfileException.Validation("body", "must be valid JSON."); }
        if (body.ValueKind != JsonValueKind.Object) throw ProfileException.Validation("body", "must be a JSON object.");
        if (!body.TryGetProperty("contentType", out var ct) || ct.ValueKind != JsonValueKind.String || !ContentTypes.Contains(ct.GetString()))
            throw ProfileException.Validation("contentType", "must be image/jpeg, image/png or image/webp.");
        if (!body.TryGetProperty("title", out var t) || t.ValueKind == JsonValueKind.Null) return null;
        if (t.ValueKind != JsonValueKind.String) throw ProfileException.Validation("title", "must be text.");
        return t.GetString()!.Length <= MaxTitleLength ? t.GetString() : throw ProfileException.Validation("title", $"allows at most {MaxTitleLength} characters.");
    }
}
