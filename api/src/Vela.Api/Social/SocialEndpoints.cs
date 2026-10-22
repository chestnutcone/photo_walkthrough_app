using Vela.Api.Auth;
using Vela.Api.Common;
using Vela.Api.Works;

namespace Vela.Api.Social;

public static class SocialEndpoints
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    public static IServiceCollection AddSocial(this IServiceCollection services) =>
        services.AddScoped<IFollowService, FollowService>();

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
        return app;
    }

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
