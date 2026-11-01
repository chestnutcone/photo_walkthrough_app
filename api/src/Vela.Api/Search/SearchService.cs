using Microsoft.EntityFrameworkCore;
using Vela.Api.Common;
using Vela.Api.Data;
using Vela.Api.Data.Entities;
using Vela.Api.Profiles;
using Vela.Api.Social;
using Vela.Api.Works;

namespace Vela.Api.Search;

/// <summary>One page of results. Only the list for the searched type is filled.</summary>
public sealed record SearchPage(IReadOnlyList<ArtistDto> Artists, IReadOnlyList<WorkDto> Stories, string? NextCursor);

public interface ISearchService
{
    Task<SearchPage> SearchAsync(Guid? viewerId, string q, SearchType type, string? cursor, int limit);
}

/// <summary>
/// Postgres search: every term must match by case-insensitive substring (pg_trgm GIN-backed ILIKE, wildcards escaped) or by
/// trigram word similarity, which tolerates typos. Results are ordered by (rank bucket desc, id asc) and paged with a keyset
/// cursor over exactly that pair, so pages never overlap or skip.
/// </summary>
public sealed class SearchService(VelaDbContext db, IVisibility visibility, ArtistQueries artists, WorkReader reader) : ISearchService
{
    public const double FuzzyThreshold = 0.4;
    private const string Escape = "\\";

    private sealed class Ranked
    {
        public Guid Id { get; init; }
        public int Rank { get; init; }
    }

    public async Task<SearchPage> SearchAsync(Guid? viewerId, string q, SearchType type, string? cursor, int limit)
    {
        var query = SearchQuery.Parse(q);
        var after = cursor is null ? (Cursor.RankedPosition?)null : Cursor.DecodeRanked(cursor);
        var ranked = type == SearchType.Artists ? await RankedArtistsAsync(query, viewerId) : await RankedStoriesAsync(query, viewerId);
        var (ids, next) = await TakePageAsync(ranked, after, limit);
        return type == SearchType.Artists
            ? new SearchPage((await artists.ByIdsAsync(ids, viewerId)).Select(r => ArtistQueries.ToDto(r, viewerId)).ToList(), [], next)
            : new SearchPage([], await LoadStoriesAsync(ids, viewerId), next);
    }

    private async Task<IQueryable<Ranked>> RankedArtistsAsync(SearchQuery query, Guid? viewerId)
    {
        var matched = await artists.VisibleProfilesAsync(viewerId);
        foreach (var term in query.Terms)
        {
            var t = term;
            matched = t.AllowFuzzy
                ? matched.Where(p => EF.Functions.ILike(p.SearchText, t.Contains, Escape) || EF.Functions.TrigramsWordSimilarity(t.Text, p.SearchText) >= FuzzyThreshold)
                : matched.Where(p => EF.Functions.ILike(p.SearchText, t.Contains, Escape));
        }
        string whole = query.Whole, prefix = query.WholePrefix;
        string c0 = query.PaddedContains[0], c1 = query.PaddedContains[1], c2 = query.PaddedContains[2], c3 = query.PaddedContains[3], c4 = query.PaddedContains[4];
        return matched.Select(p => new Ranked { Id = p.UserId, Rank =
            p.Handle == whole ? 3
            : EF.Functions.ILike(p.Handle, prefix, Escape) || EF.Functions.ILike(p.DisplayName, prefix, Escape) ? 2
            : EF.Functions.ILike(p.SearchText, c0, Escape) && EF.Functions.ILike(p.SearchText, c1, Escape) && EF.Functions.ILike(p.SearchText, c2, Escape)
              && EF.Functions.ILike(p.SearchText, c3, Escape) && EF.Functions.ILike(p.SearchText, c4, Escape) ? 1
            : 0 });
    }

    private async Task<IQueryable<Ranked>> RankedStoriesAsync(SearchQuery query, Guid? viewerId)
    {
        var matched = WorkVisibility.Public(db, await visibility.HiddenUserIdsAsync(viewerId)).AsNoTracking();
        foreach (var term in query.Terms)
        {
            var t = term;
            matched = t.AllowFuzzy
                ? matched.Where(w => EF.Functions.ILike(w.SearchText, t.Contains, Escape) || EF.Functions.TrigramsWordSimilarity(t.Text, w.SearchText) >= FuzzyThreshold)
                : matched.Where(w => EF.Functions.ILike(w.SearchText, t.Contains, Escape));
        }
        string prefix = query.WholePrefix, contains = query.WholeContains;
        string c0 = query.PaddedContains[0], c1 = query.PaddedContains[1], c2 = query.PaddedContains[2], c3 = query.PaddedContains[3], c4 = query.PaddedContains[4];
        return matched.Select(w => new Ranked { Id = w.Id, Rank =
            EF.Functions.ILike(w.Title, prefix, Escape) ? 3
            : EF.Functions.ILike(w.Title, contains, Escape) ? 2
            : EF.Functions.ILike(w.SearchText, c0, Escape) && EF.Functions.ILike(w.SearchText, c1, Escape) && EF.Functions.ILike(w.SearchText, c2, Escape)
              && EF.Functions.ILike(w.SearchText, c3, Escape) && EF.Functions.ILike(w.SearchText, c4, Escape) ? 1
            : 0 });
    }

    private static async Task<(List<Guid> Ids, string? Next)> TakePageAsync(IQueryable<Ranked> ranked, Cursor.RankedPosition? after, int limit)
    {
        if (after is { } pos)
        {
            int rank = pos.Rank; Guid id = pos.Id;
            ranked = ranked.Where(x => x.Rank < rank || (x.Rank == rank && x.Id.CompareTo(id) > 0));
        }
        var rows = await ranked.OrderByDescending(x => x.Rank).ThenBy(x => x.Id).Take(limit + 1).ToListAsync();
        var page = rows.Take(limit).ToList();
        var next = rows.Count > limit ? Cursor.EncodeRanked(page[^1].Rank, page[^1].Id) : null;
        return (page.Select(x => x.Id).ToList(), next);
    }

    private async Task<IReadOnlyList<WorkDto>> LoadStoriesAsync(List<Guid> ids, Guid? viewerId)
    {
        var byId = await db.Works.AsNoTracking().Where(w => ids.Contains(w.Id)).ToDictionaryAsync(w => w.Id);
        return await reader.BuildAsync(ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList(), viewerId);
    }
}
