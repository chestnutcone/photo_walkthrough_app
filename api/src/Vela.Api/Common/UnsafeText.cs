namespace Vela.Api.Common;

/// <summary>
/// Characters that change how surrounding text is displayed rather than being text: bidirectional embeddings, overrides and
/// isolates (U+202A-202E, U+2066-2069), which can make "photo-gpj.exe" read as "photo-exe.jpg" or reorder a reply; and the line and
/// paragraph separators U+2028/2029, which some renderers treat as line breaks that validators and editors do not see.
/// Right-to-left <em>text</em> needs none of them (Arabic and Hebrew letters are strongly directional, and the marks
/// U+200E/U+200F stay allowed), so they are refused in every user-supplied text field.
/// </summary>
public static class UnsafeText
{
    public static bool IsDisallowed(int codePoint) =>
        codePoint is (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069) or 0x2028 or 0x2029;

    public static bool ContainsDisallowed(string? text) => text is not null && text.Any(c => IsDisallowed(c));
}
