using System.Globalization;
using System.Text;

namespace Vela.Api.Common;

/// <summary>
/// The one cleaner for user-written text (S2-2): display names, bios, titles, pin notes, replies, inquiries and report notes.
/// The app mirrors it exactly in <c>app/lib/core/text/text_sanitizer.dart</c>; both run the shared vectors in
/// <c>contract/text-sanitizer-vectors.json</c>, so the length a person sees counted while typing is the length the server checks.
/// <list type="number">
/// <item>Lone surrogates are dropped, then the text is normalised to NFC.</item>
/// <item>Control characters are dropped; tabs and every Unicode space become a plain space (new lines too, unless multi-line).</item>
/// <item>Invisible format characters are dropped (zero-width space, BOM, word joiner, soft hyphen, bidi controls, Hangul
/// fillers). Kept: LRM/RLM/ALM, the Arabic number signs, emoji tag sequences after U+1F3F4, ZWJ inside an emoji sequence, and
/// ZWJ/ZWNJ between two letters or marks of a script beyond Latin, Greek, Cyrillic and Armenian (Indic conjuncts, Persian).</item>
/// <item>At most <see cref="MaxStackedMarks"/> non-spacing or enclosing marks in a row (stops zalgo); the rest are dropped.</item>
/// <item>Runs of spaces become one space and the text is trimmed; multi-line text also trims each line and keeps at most one
/// blank line between paragraphs.</item>
/// </list>
/// Length is counted in Unicode code points of the cleaned text (<see cref="Length"/>): Dart and .NET agree on code points,
/// while grapheme segmentation differs between their Unicode tables.
/// </summary>
public static class TextSanitizer
{
    public const int MaxStackedMarks = 4;

    /// <summary>Code points of already-cleaned text: the length every limit is checked against, here and in the app.</summary>
    public static int Length(string text)
    {
        var n = 0;
        foreach (var _ in text.EnumerateRunes()) n++;
        return n;
    }

    public static string Clean(string? raw, bool multiline = false)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var runes = Decode(raw);
        var kept = Filter(runes, multiline);
        return multiline ? CollapseLines(kept) : CollapseSpaces(kept).Trim(' ');
    }

    /// <summary>Valid runes only (lone surrogates dropped), NFC, and CR/CRLF folded to LF.</summary>
    private static List<int> Decode(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (char.IsHighSurrogate(c) && i + 1 < raw.Length && char.IsLowSurrogate(raw[i + 1])) { sb.Append(c).Append(raw[++i]); continue; }
            if (char.IsSurrogate(c)) continue;
            sb.Append(c);
        }
        var normal = sb.ToString().Normalize(NormalizationForm.FormC).Replace("\r\n", "\n").Replace('\r', '\n');
        var list = new List<int>(normal.Length);
        foreach (var r in normal.EnumerateRunes()) list.Add(r.Value);
        return list;
    }

    private static List<int> Filter(List<int> cps, bool multiline)
    {
        var output = new List<int>(cps.Count);
        var marks = 0;
        for (var i = 0; i < cps.Count; i++)
        {
            var cp = cps[i];
            var prev = output.Count > 0 ? output[^1] : -1;
            var next = i + 1 < cps.Count ? cps[i + 1] : -1;
            var mapped = Map(cp, prev, next, multiline);
            if (mapped < 0) continue;
            if (IsStackingMark(mapped))
            {
                if (++marks > MaxStackedMarks) continue;
            }
            else marks = 0;
            output.Add(mapped);
        }
        return output;
    }

    /// <summary>The code point to emit for <paramref name="cp"/>, or -1 to drop it.</summary>
    private static int Map(int cp, int prev, int next, bool multiline)
    {
        if (cp == '\n') return multiline ? '\n' : ' ';
        if (IsSpace(cp)) return ' ';
        var category = Category(cp);
        if (category == UnicodeCategory.Control) return -1;
        if (cp is 0x115F or 0x1160 or 0x3164 or 0xFFA0) return -1; // Hangul fillers render blank
        if (cp == 0x200D) return (IsEmojiPart(prev) && IsEmoji(next)) || (IsJoinableLetter(prev) && IsJoinableLetter(next)) ? cp : -1;
        if (cp == 0x200C) return IsJoinableLetter(prev) && IsJoinableLetter(next) ? cp : -1;
        if (cp is >= 0xE0020 and <= 0xE007F) return prev == 0x1F3F4 || prev is >= 0xE0020 and <= 0xE007E ? cp : -1;
        if (category == UnicodeCategory.Format) return KeepFormat(cp) ? cp : -1;
        return cp;
    }

    private static bool KeepFormat(int cp) =>
        cp is 0x200E or 0x200F or 0x061C or (>= 0x0600 and <= 0x0605) or 0x06DD or 0x070F or 0x0890 or 0x0891 or 0x08E2
            or 0x110BD or 0x110CD;

    private static bool IsSpace(int cp) =>
        cp is (>= 0x09 and <= 0x0D) or 0x20 or 0x85 or 0xA0 or 0x1680 or (>= 0x2000 and <= 0x200A) or 0x2028 or 0x2029 or 0x202F
            or 0x205F or 0x3000;

    private static bool IsStackingMark(int cp) => Category(cp) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark;

    /// <summary>A letter or mark of a script that uses ZWJ/ZWNJ (from Hebrew, U+0590, upwards).</summary>
    private static bool IsJoinableLetter(int cp) =>
        cp >= 0x0590 && Category(cp) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    /// <summary>Emoji by fixed ranges, identical in the app (Dart has no emoji property in its regular expressions).</summary>
    private static bool IsEmoji(int cp) =>
        cp is (>= 0x1F000 and <= 0x1FAFF) or (>= 0x2600 and <= 0x27BF) or (>= 0x2300 and <= 0x23FF) or (>= 0x2B00 and <= 0x2BFF)
            or (>= 0x2194 and <= 0x21AA) or 0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139 or 0x3030 or 0x303D or 0x3297 or 0x3299;

    /// <summary>What may end the emoji before a ZWJ: an emoji, the emoji presentation selector or a keycap.</summary>
    private static bool IsEmojiPart(int cp) => IsEmoji(cp) || cp is 0xFE0F or 0x20E3;

    private static UnicodeCategory Category(int cp) => cp < 0 ? UnicodeCategory.OtherNotAssigned : CharUnicodeInfo.GetUnicodeCategory(cp);

    private static string CollapseSpaces(IEnumerable<int> cps)
    {
        var sb = new StringBuilder();
        var lastSpace = false;
        foreach (var cp in cps)
        {
            if (cp == ' ' && lastSpace) continue;
            lastSpace = cp == ' ';
            sb.Append(char.ConvertFromUtf32(cp));
        }
        return sb.ToString();
    }

    private static string CollapseLines(List<int> cps)
    {
        var lines = CollapseSpaces(cps).Split('\n').Select(l => l.Trim(' ')).ToList();
        var sb = new StringBuilder();
        var blanks = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0) { blanks++; continue; }
            if (sb.Length > 0) sb.Append(blanks > 0 ? "\n\n" : "\n");
            sb.Append(line);
            blanks = 0;
        }
        return sb.ToString();
    }
}
