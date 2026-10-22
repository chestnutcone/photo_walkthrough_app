using System.Globalization;
using System.Net;
using System.Text;

namespace Vela.Api.Share;

/// <summary>
/// The only way dynamic values enter markup: cleaned of control and line-separator characters, then HTML-encoded.
/// The result is safe in both text and double-quoted attribute contexts.
/// </summary>
public static class HtmlText
{
    public static string E(string? value) => WebUtility.HtmlEncode(Clean(value));

    /// <summary>Replaces control characters and unicode line/paragraph separators with spaces and collapses whitespace runs.</summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(value.Length);
        var lastSpace = true;
        foreach (var ch in value)
        {
            var category = char.GetUnicodeCategory(ch);
            var space = char.IsWhiteSpace(ch) || char.IsControl(ch)
                || category is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
            if (space)
            {
                if (!lastSpace) sb.Append(' ');
                lastSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastSpace = false;
            }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Cuts to at most <paramref name="max"/> UTF-16 units without leaving half a surrogate pair.</summary>
    public static string Truncate(string text, int max)
    {
        if (text.Length <= max) return text;
        var cut = max;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut].TrimEnd();
    }

    /// <summary>Cleaned and truncated, not yet encoded.</summary>
    public static string Short(string? value, int max) => Truncate(Clean(value), max);
}
