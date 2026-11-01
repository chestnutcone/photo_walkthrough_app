using System.Text.Json;
using Vela.Api.Auth;
using Vela.Api.Common;
using Vela.Api.Social;

namespace Vela.Api.Notifications;

public static class NotificationEndpoints
{
    public const int MaxIdsPerRequest = 200;

    public static IServiceCollection AddNotifications(this IServiceCollection services) =>
        services.AddScoped<INotificationService, NotificationService>()
            .AddScoped<IDomainEventHandler<ReplyAdded>, ReplyNotificationHandler>()
            .AddScoped<IDomainEventHandler<Followed>, FollowerNotificationHandler>()
            .AddScoped<IDomainEventHandler<WorkPublished>, NewStoryNotificationHandler>();

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/notifications", (string? cursor, string? limit, ICurrentUser me, INotificationService svc) =>
            SocialEndpoints.Run(async () =>
            {
                var page = await svc.ListAsync(me.UserId!.Value, cursor, SocialEndpoints.ParseLimit(limit));
                return Common.Results.Ok(page.Items, page.NextCursor);
            })).RequireAuthorization().WithName("listNotifications");
        app.MapGet("/v1/notifications/unread-count", (ICurrentUser me, INotificationService svc) =>
            SocialEndpoints.Run(async () => Common.Results.Ok(new { count = await svc.UnreadCountAsync(me.UserId!.Value) })))
            .RequireAuthorization().WithName("unreadCount");
        app.MapPost("/v1/notifications/read", (HttpContext ctx, ICurrentUser me, INotificationService svc) =>
            SocialEndpoints.Run(async () =>
            {
                await svc.MarkReadAsync(me.UserId!.Value, await ReadIdsAsync(ctx));
                return Common.Results.Ok<object?>(null);
            })).RequireAuthorization().WithName("markRead");
        return app;
    }

    /// <summary>The optional "ids" list of the body; absent or null means "all".</summary>
    private static async Task<IReadOnlyList<Guid>?> ReadIdsAsync(HttpContext ctx)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid("Body must be a JSON object.");
            if (!root.TryGetProperty("ids", out var ids) || ids.ValueKind == JsonValueKind.Null) return null;
            if (ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() > MaxIdsPerRequest)
                throw Invalid($"ids must be a list of at most {MaxIdsPerRequest} ids.");
            return ids.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String && Guid.TryParse(e.GetString(), out var id) ? id : throw Invalid("ids must contain valid ids."))
                .ToList();
        }
        catch (JsonException) { throw Invalid("Body must be valid JSON."); }
    }

    private static SafetyException Invalid(string message) => new(StatusCodes.Status400BadRequest, "validation_failed", message);
}
