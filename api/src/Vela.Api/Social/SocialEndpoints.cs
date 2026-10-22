using System.Text.Json;
using Vela.Api.Auth;
using Vela.Api.Common;
using Vela.Api.Feed;
using Vela.Api.Works;

namespace Vela.Api.Social;

public static class SocialEndpoints
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    public static IServiceCollection AddSocial(this IServiceCollection services) =>
        services.AddScoped<IFollowService, FollowService>().AddScoped<IFeedService, FeedService>()
            .AddScoped<IReplyService, ReplyService>();

    public static IEndpointRouteBuilder MapSocialEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/v1/artists/{id}/follow", (string id, ICurrentUser me, IFollowService svc) =>
            Run(async () => { await svc.FollowAsync(me.UserId!.Value, ParseArtist(id)); return Common.Results.Ok<object?>(null); }))
            .RequireAuthorization().WithName("follow");
        app.MapDelete("/v1/artists/{id}/follow", (string id, ICurrentUser me, IFollowService svc) =>
            Run(async () => { await svc.UnfollowAsync(me.UserId!.Value, ParseArtist(id)); return Common.Results.Ok<object?>(null); }))
            .RequireAuthorization().WithName("unfollow");
        app.MapGet("/v1/artists/{id}/followers", (string id, string? cursor, string? limit, ICurrentUser me, IFollowService svc) =>
            Run(async () =>
            {
                var page = await svc.ListFollowersAsync(me.UserId, ParseArtist(id), cursor, ParseLimit(limit));
                return Common.Results.Ok(page.Items, page.NextCursor);
            })).RequireAuthorization().WithName("listFollowers");
        app.MapGet("/v1/feed", (string? tab, string? cursor, string? limit, ICurrentUser me, IFeedService svc) =>
            Run(async () =>
            {
                var page = await svc.GetAsync(me.UserId, ParseTab(tab), cursor, ParseLimit(limit));
                return Common.Results.Ok(page.Items, page.NextCursor);
            })).WithName("getFeed");
        app.MapGet("/v1/artists/{handle}/works", (string handle, string? cursor, string? limit, ICurrentUser me, IFeedService svc) =>
            Run(async () =>
            {
                var page = await svc.ListArtistWorksAsync(me.UserId, handle, cursor, ParseLimit(limit));
                return Common.Results.Ok(page.Items, page.NextCursor);
            })).WithName("listArtistWorks");
        MapReplies(app);
        return app;
    }

    private static void MapReplies(IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/works/{id}/chapters/{index}/replies", (string id, string index, string? cursor, string? limit, ICurrentUser me, IReplyService svc) =>
            Run(async () =>
            {
                var page = await svc.ListAsync(me.UserId, ParseWork(id), ParseIndex(index), cursor, ParseLimit(limit));
                return Common.Results.Ok(page.Items, page.NextCursor);
            })).WithName("listReplies");
        app.MapPost("/v1/works/{id}/chapters/{index}/replies", (string id, string index, HttpContext ctx, ICurrentUser me, IReplyService svc) =>
            Run(async () =>
            {
                var reply = await svc.AddAsync(me.UserId!.Value, ParseWork(id), ParseIndex(index), await ReadBodyAsync(ctx));
                return Common.Results.Created(reply);
            })).RequireAuthorization().WithName("addReply");
        app.MapDelete("/v1/replies/{id}", (string id, ICurrentUser me, IReplyService svc) =>
            Run(async () =>
            {
                await svc.DeleteAsync(me.UserId!.Value, Guid.TryParse(id, out var rid) ? rid : throw ReplyService.ReplyNotFound());
                return Common.Results.Ok<object?>(null);
            })).RequireAuthorization().WithName("deleteReply");
    }

    private static Guid ParseWork(string raw) => Guid.TryParse(raw, out var id) ? id : throw ReplyService.WorkNotFound();

    private static int ParseIndex(string raw) => int.TryParse(raw, out var i) && i >= 0 ? i : throw ReplyService.ChapterNotFound();

    /// <summary>The "body" text of the request; anything that is not a JSON object with a text body is a validation error.</summary>
    private static async Task<string> ReadBodyAsync(HttpContext ctx)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String)
                return body.GetString()!;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { }
        throw new SafetyException(StatusCodes.Status400BadRequest, "validation_failed", "body must be text.");
    }

    private static FeedTab ParseTab(string? raw) => raw switch
    {
        "following" => FeedTab.Following,
        "discover" => FeedTab.Discover,
        _ => throw new SafetyException(StatusCodes.Status400BadRequest, "validation_failed", "tab must be following or discover."),
    };

    public static Guid ParseArtist(string raw) => Guid.TryParse(raw, out var id) ? id : throw FollowService.NotFound();

    /// <summary>Page size: default 20, clamped to 1..50; text that is not a number is a 400.</summary>
    public static int ParseLimit(string? raw)
    {
        if (raw is null) return DefaultLimit;
        if (!int.TryParse(raw, out var n)) throw new SafetyException(StatusCodes.Status400BadRequest, "validation_failed", "limit must be a whole number.");
        return Math.Clamp(n, 1, MaxLimit);
    }

    /// <summary>Maps the social/work/cursor exceptions onto the standard error envelope.</summary>
    public static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (SafetyException e) { return Common.Results.Fail(e.Status, e.Code, e.Message); }
        catch (WorkException e) { return Common.Results.Fail(e.Status, e.Code, e.Message); }
        catch (InvalidCursorException) { return Common.Results.Fail(StatusCodes.Status400BadRequest, "invalid_cursor", "The cursor is not valid."); }
    }
}
