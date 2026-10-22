using Vela.Api.Auth;
using Vela.Api.Social;

namespace Vela.Api.Search;

public sealed record SearchResultsDto(IReadOnlyList<Profiles.ArtistDto> Artists, IReadOnlyList<Works.WorkDto> Stories);

public static class SearchEndpoints
{
    public static IServiceCollection AddSearch(this IServiceCollection services) => services.AddScoped<ISearchService, SearchService>();

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/search", (string? q, string? type, string? cursor, string? limit, ICurrentUser me, ISearchService svc) =>
            SocialEndpoints.Run(async () =>
            {
                try
                {
                    var page = await svc.SearchAsync(me.UserId, q ?? "", ParseType(type), cursor, SocialEndpoints.ParseLimit(limit));
                    return Common.Results.Ok(new SearchResultsDto(page.Artists, page.Stories), page.NextCursor);
                }
                catch (SearchException e) { return Common.Results.Fail(StatusCodes.Status400BadRequest, e.Code, e.Message); }
            })).WithName("search");
        return app;
    }

    private static SearchType ParseType(string? raw) => raw switch
    {
        null or "artists" => SearchType.Artists,
        "stories" => SearchType.Stories,
        _ => throw new SearchException("validation_failed", "type must be artists or stories."),
    };
}
