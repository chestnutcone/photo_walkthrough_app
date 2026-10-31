using System.Text.Json;
using Vela.Api.Common;

namespace Vela.Api.Tests.Common;

/// <summary>
/// The sanitizer every user-text field goes through (S2-2). The vectors in <c>contract/text-sanitizer-vectors.json</c> are shared
/// with the app's <c>text_sanitizer_test.dart</c>, so the client counts and cleans exactly as the server does.
/// </summary>
public class TextSanitizerTests
{
    public sealed record Vector(string Name, string Input, bool Multiline, string Expected, int Length)
    {
        public override string ToString() => Name;
    }

    private static readonly Lazy<JsonDocument> Doc = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "contract", "text-sanitizer-vectors.json"))) dir = dir.Parent;
        if (dir is null) throw new FileNotFoundException("contract/text-sanitizer-vectors.json not found above the test output.");
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(dir.FullName, "contract", "text-sanitizer-vectors.json")));
    });

    private static List<Vector> Load() =>
        Doc.Value.RootElement.GetProperty("vectors").EnumerateArray()
            .Select(v => new Vector(v.GetProperty("name").GetString()!, v.GetProperty("input").GetString()!, v.GetProperty("multiline").GetBoolean(),
                v.GetProperty("expected").GetString()!, v.GetProperty("length").GetInt32()))
            .ToList();

    public static TheoryData<Vector> Vectors()
    {
        var data = new TheoryData<Vector>();
        foreach (var v in Load()) data.Add(v);
        return data;
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Shared_vector_cleans_and_counts_like_the_app(Vector v)
    {
        var cleaned = TextSanitizer.Clean(v.Input, v.Multiline);

        Assert.Equal(v.Expected, cleaned);
        Assert.Equal(v.Length, TextSanitizer.Length(cleaned));
    }

    [Fact]
    public void Shared_mark_cap_matches_the_code() =>
        Assert.Equal(TextSanitizer.MaxStackedMarks, Doc.Value.RootElement.GetProperty("maxStackedMarks").GetInt32());

    [Fact]
    public void Lone_surrogates_are_dropped_instead_of_throwing_in_normalisation()
    {
        Assert.Equal("ab", TextSanitizer.Clean("a\uD800b"));
        Assert.Equal("ab", TextSanitizer.Clean("a\uDC00b"));
    }

    [Fact]
    public void Null_is_empty() => Assert.Equal("", TextSanitizer.Clean(null));

    [Fact]
    public void Very_long_text_is_cleaned_in_linear_time_and_counted_in_code_points()
    {
        var text = string.Concat(Enumerable.Repeat("\U0001F600 ", 50_000));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var cleaned = TextSanitizer.Clean(text);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"took {sw.Elapsed}");
        Assert.Equal(100_000 - 1, TextSanitizer.Length(cleaned));
        Assert.Equal(150_000 - 1, cleaned.Length); // UTF-16 length differs: each emoji is a surrogate pair
    }

    [Fact]
    public void Clean_is_idempotent_on_every_vector()
    {
        foreach (var v in Load())
            Assert.Equal(v.Expected, TextSanitizer.Clean(v.Expected, v.Multiline));
    }
}
