using Vela.Api.Search;

namespace Vela.Api.Tests.Search;

/// <summary>S2 fix (review M-8): the query is cleaned like the stored text (NFC, invisible characters removed) before matching.</summary>
public class SearchQueryNormalisationTests
{
    [Fact]
    public void Decomposed_query_is_composed_like_the_stored_text() =>
        Assert.Equal("café", SearchQuery.Parse("café").Whole);

    [Fact]
    public void Zero_width_and_control_characters_are_removed() =>
        Assert.Equal("harbour light", SearchQuery.Parse("har​bour\u0007 light").Whole);

    [Fact]
    public void Unicode_spaces_split_terms() =>
        Assert.Equal(["low", "tide"], SearchQuery.Parse("low　tide").Terms.Select(t => t.Text));

    [Fact]
    public void A_query_that_is_only_invisible_is_too_short()
    {
        var e = Assert.Throws<SearchException>(() => SearchQuery.Parse("​​​"));
        Assert.Equal("query_too_short", e.Code);
    }

    [Fact]
    public void Nul_is_still_refused() =>
        Assert.Equal("validation_failed", Assert.Throws<SearchException>(() => SearchQuery.Parse("ab\0cd")).Code);
}
