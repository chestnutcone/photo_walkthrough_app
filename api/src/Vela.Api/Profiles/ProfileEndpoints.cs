using System.Text.Json;
using Vela.Api.Auth;
using Vela.Api.Works;

namespace Vela.Api.Profiles;

public static class ProfileEndpoints
{
    public static IServiceCollection AddProfiles(this IServiceCollection services) =>
        services.AddScoped<WorkCounts>().AddScoped<ArtistQueries>().AddScoped<IProfileService, ProfileService>();

    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/me", (ICurrentUser me, IProfileService svc) =>
            Run(async () => Results.Ok(await svc.GetMeAsync(me.UserId!.Value)))).RequireAuthorization().WithName("getMe");

        app.MapPost("/v1/me/onboarding", (HttpContext ctx, ICurrentUser me, IProfileService svc) =>
            Run(async () =>
            {
                var req = ProfileRequestParser.ParseOnboarding(await ReadBodyAsync(ctx));
                await svc.CompleteOnboardingAsync(me.UserId!.Value, req);
                return Results.Ok(await svc.GetMeAsync(me.UserId.Value));
            })).RequireAuthorization().WithName("completeOnboarding");

        app.MapPatch("/v1/me/profile", (HttpContext ctx, ICurrentUser me, IProfileService svc) =>
            Run(async () =>
            {
                var req = ProfileRequestParser.ParseUpdate(await ReadBodyAsync(ctx));
                await svc.UpdateAsync(me.UserId!.Value, req);
                return Results.Ok(await svc.GetMeAsync(me.UserId.Value));
            })).RequireAuthorization().WithName("updateProfile");

        // Public with optional auth: a valid bearer token only adds isFollowing and the exclusions.
        app.MapGet("/v1/artists/suggested", (ICurrentUser me, IProfileService svc) =>
            Run(async () => Results.Ok(await svc.GetSuggestedAsync(me.UserId)))).WithName("suggestedArtists");

        app.MapGet("/v1/artists/{handle}", (string handle, ICurrentUser me, IProfileService svc) =>
            Run(async () => await svc.GetByHandleAsync(handle, me.UserId) is { } artist
                ? Results.Ok(artist)
                : Results.Fail(StatusCodes.Status404NotFound, "artist_not_found", "No artist with that handle."))).WithName("getArtist");

        return app;
    }

    private static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (ProfileException e) { return Results.Fail(e.Status, e.Code, e.Message); }
    }

    private static async Task<JsonElement> ReadBodyAsync(HttpContext ctx)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw ProfileException.Validation("body", "must be valid JSON.");
        }
    }
}
