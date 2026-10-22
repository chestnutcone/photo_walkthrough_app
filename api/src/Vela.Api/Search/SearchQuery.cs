namespace Vela.Api.Search;

public enum SearchType { Artists, Stories }

/// <summary>A search the caller got wrong: too short, too long, bad type. Maps to a 400.</summary>
public sealed class SearchException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// A validated search text: at most <see cref="MaxTerms"/> whitespace-separated terms, each lower-cased and carrying its
/// LIKE-escaped pattern. Nothing from the user is ever concatenated into SQL; patterns travel as parameters.
/// </summary>
public sealed class SearchQuery
{
    public const int MinLength = 2;
    public const int MaxLength = 80;
    public const int MaxTerms = 5;
    /// <summary>Terms shorter than this only match by substring; trigram similarity on one or two letters is noise.</summary>
    public const int MinFuzzyTermLength = 3;
    public const char LikeEscape = '\\';

    public sealed record Term(string Text, string Contains, bool AllowFuzzy);

    public IReadOnlyList<Term> Terms { get; }
    /// <summary>Exactly <see cref="MaxTerms"/> contains-patterns; unused slots are "%" (always true), so ranking can be one static expression.</summary>
    public IReadOnlyList<string> PaddedContains { get; }
    /// <summary>The normalized whole query (terms joined by one space) and its LIKE patterns, for ranking.</summary>
    public string Whole { get; }
    public string WholePrefix { get; }
    public string WholeContains { get; }

    private SearchQuery(IReadOnlyList<Term> terms, string whole)
    {
        Terms = terms;
        PaddedContains = terms.Select(t => t.Contains).Concat(Enumerable.Repeat("%", MaxTerms - terms.Count)).ToList();
        Whole = whole;
        WholePrefix = EscapeLike(whole) + "%";
        WholeContains = "%" + EscapeLike(whole) + "%";
    }

    public static SearchQuery Parse(string? raw)
    {
        var q = (raw ?? "").Trim();
        if (q.Length < MinLength) throw new SearchException("query_too_short", $"Type at least {MinLength} characters to search.");
        if (q.Length > MaxLength) throw new SearchException("validation_failed", $"Search text can be at most {MaxLength} characters.");
        if (q.Contains('\0')) throw new SearchException("validation_failed", "Search text contains an invalid character.");

        var words = q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(MaxTerms)
            .Select(w => w.ToLowerInvariant()).ToList();
        var terms = words.Select(w => new Term(w, "%" + EscapeLike(w) + "%", w.Length >= MinFuzzyTermLength)).ToList();
        return new SearchQuery(terms, string.Join(' ', words));
    }

    /// <summary>Escapes the LIKE metacharacters so user text always matches literally.</summary>
    public static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
