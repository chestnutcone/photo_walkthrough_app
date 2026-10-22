using System.Text.RegularExpressions;

namespace Vela.Api.Tests.Admin;

/// <summary>A deliberately small tag scanner: enough to prove which elements and attributes a page really contains.</summary>
public static partial class HtmlInspector
{
    public sealed record Tag(string Name, IReadOnlyDictionary<string, string> Attributes);

    [GeneratedRegex("<([a-zA-Z][a-zA-Z0-9-]*)((?:\\s+[^\\s=>/\"']+(?:\\s*=\\s*(?:\"[^\"]*\"|'[^']*'|[^\\s>\"']+))?)*)\\s*/?>")]
    private static partial Regex TagPattern();

    [GeneratedRegex("([^\\s=>/\"']+)(?:\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>\"']+)))?")]
    private static partial Regex AttrPattern();

    [GeneratedRegex("<style[^>]*>.*?</style>", RegexOptions.Singleline)]
    private static partial Regex StyleBlock();

    public static List<Tag> Tags(string html) =>
        TagPattern().Matches(StyleBlock().Replace(html, "<style></style>")).Select(m => new Tag(m.Groups[1].Value.ToLowerInvariant(),
            AttrPattern().Matches(m.Groups[2].Value).ToDictionary(a => a.Groups[1].Value.ToLowerInvariant(),
                a => a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Value, StringComparer.Ordinal))).ToList();

    /// <summary>No script-capable element, no event-handler attribute, no inline style attribute, no javascript: URL.</summary>
    public static void AssertInert(string html)
    {
        var tags = Tags(html);
        Assert.DoesNotContain(tags, t => t.Name is "script" or "iframe" or "object" or "embed" or "base" or "link" or "svg" or "math");
        Assert.DoesNotContain(tags, t => t.Attributes.Keys.Any(k => k.StartsWith("on", StringComparison.Ordinal)));
        Assert.DoesNotContain(tags, t => t.Attributes.ContainsKey("style"));
        Assert.DoesNotContain(tags, t => t.Attributes.Values.Any(v => v.TrimStart().StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)));
    }
}
