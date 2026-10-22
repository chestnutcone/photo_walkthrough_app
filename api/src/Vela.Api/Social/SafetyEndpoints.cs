using System.Text.Json;
using Vela.Api.Auth;

namespace Vela.Api.Social;

public static class SafetyEndpoints
{
    private const int MaxNote = 500;
    private static readonly string[] TargetTypes = ["work", "reply", "user"];
    private static readonly string[] Reasons = ["illegal_content", "copyright", "harassment", "spam", "other"];

    public static IServiceCollection AddSafety(this IServiceCollection services, IConfiguration config) =>
        services.Configure<ReportOptions>(config.GetSection(ReportOptions.Section))
            .AddScoped<IVisibility, Visibility>().AddScoped<IBlockService, BlockService>()
            .AddScoped<ReportService>().AddScoped<IReportService>(sp => sp.GetRequiredService<ReportService>());

    public static IEndpointRouteBuilder MapSafetyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/v1/blocks/{userId}", (string userId, ICurrentUser me, IBlockService svc, HttpContext ctx) =>
            Run(ctx, async () => { await svc.BlockAsync(me.UserId!.Value, Target(userId)); return Common.Results.Ok<object?>(null); }))
            .RequireAuthorization().WithName("block");

        app.MapDelete("/v1/blocks/{userId}", (string userId, ICurrentUser me, IBlockService svc, HttpContext ctx) =>
            Run(ctx, async () => { await svc.UnblockAsync(me.UserId!.Value, Target(userId)); return Common.Results.Ok<object?>(null); }))
            .RequireAuthorization().WithName("unblock");

        app.MapPost("/v1/reports", (HttpContext ctx, ICurrentUser me, IReportService svc) =>
            Run(ctx, async () =>
            {
                var (type, id, reason, note) = await ParseReportAsync(ctx);
                await svc.CreateAsync(me.UserId!.Value, type, id, reason, note);
                return Common.Results.Ok<object?>(null);
            })).RequireAuthorization().WithName("report");

        return app;
    }

    private static Guid Target(string raw) =>
        Guid.TryParse(raw, out var id) ? id : throw new SafetyException(StatusCodes.Status404NotFound, "user_not_found", "No such user.");

    private static async Task<IResult> Run(HttpContext ctx, Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (SafetyException e)
        {
            if (e.RetryAfter is { } wait) ctx.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
            return Common.Results.Fail(e.Status, e.Code, e.Message);
        }
    }

    private static async Task<(string Type, Guid Id, string Reason, string? Note)> ParseReportAsync(HttpContext ctx)
    {
        JsonElement root;
        try
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
            root = doc.RootElement.Clone();
        }
        catch (JsonException) { throw Invalid("Body must be valid JSON."); }
        if (root.ValueKind != JsonValueKind.Object) throw Invalid("Body must be a JSON object.");

        var type = Str(root, "targetType");
        var reason = Str(root, "reason");
        if (type is null || !TargetTypes.Contains(type)) throw Invalid("targetType must be work, reply or user.");
        if (reason is null || !Reasons.Contains(reason)) throw Invalid("reason is not a valid report reason.");
        if (Str(root, "targetId") is not { } rawId || !Guid.TryParse(rawId, out var id)) throw Invalid("targetId must be a UUID.");
        string? note = null;
        if (root.TryGetProperty("note", out var n) && n.ValueKind != JsonValueKind.Null)
        {
            if (n.ValueKind != JsonValueKind.String || n.GetString()!.Length > MaxNote) throw Invalid($"note must be at most {MaxNote} characters.");
            note = n.GetString();
        }
        return (type, id, reason, note);
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static SafetyException Invalid(string message) => new(StatusCodes.Status400BadRequest, "validation_failed", message);
}
