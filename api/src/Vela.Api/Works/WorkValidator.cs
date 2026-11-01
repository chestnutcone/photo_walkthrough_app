using Vela.Api.Common;

namespace Vela.Api.Works;

/// <summary>
/// Request limits for updateWork. Text is cleaned by <see cref="TextSanitizer"/> (S2-2) and the limits apply to the cleaned text
/// in code points, as the app counts it; bidi controls are refused outright. Messages name the field only.
/// </summary>
public static class WorkValidator
{
    public const int MaxTitle = 200, MaxIntro = 1000, MaxAltText = 300, MaxChapters = 40;
    public const int MaxChapterTitle = 120, MaxChapterBody = 1000, MaxChapterMeta = 80;
    public const double MinZoom = 1.0, MaxZoom = 8.0;

    /// <summary>The request with every text field cleaned, or a validation error.</summary>
    public static UpdateWorkRequest Validate(UpdateWorkRequest? req)
    {
        if (req is null) throw WorkException.Validation("body", "must be a JSON object.");
        var title = Text(req.Title, "title", 1, MaxTitle, required: true);
        var intro = Text(req.Intro, "intro", 0, MaxIntro, multiline: true);
        var alt = Text(req.AltText, "altText", 0, MaxAltText);
        if (req.Chapters is null) throw WorkException.Validation("chapters", "is required.");
        if (req.Chapters.Count > MaxChapters) throw WorkException.Validation("chapters", $"allows at most {MaxChapters} items.");
        var chapters = req.Chapters.Select((c, i) => (ChapterInput?)Chapter(c, $"chapters[{i}]")).ToList();
        return new UpdateWorkRequest(title, intro, alt, chapters);
    }

    /// <summary>The cleaned draft title from createDraft (optional there), or a validation error.</summary>
    public static string? DraftTitle(string? raw) => Text(raw, "title", 0, MaxTitle);

    private static ChapterInput Chapter(ChapterInput? c, string path)
    {
        if (c is null) throw WorkException.Validation(path, "must be an object.");
        Unit(c.X, $"{path}.x");
        Unit(c.Y, $"{path}.y");
        if (c.Zoom is { } z && !(z >= MinZoom && z <= MaxZoom))
            throw WorkException.Validation($"{path}.zoom", $"must be null or between {MinZoom:0.0} and {MaxZoom:0.0}.");
        return c with
        {
            Title = Text(c.Title, $"{path}.title", 1, MaxChapterTitle, required: true),
            Body = Text(c.Body, $"{path}.body", 0, MaxChapterBody, multiline: true),
            Meta = Text(c.Meta, $"{path}.meta", 0, MaxChapterMeta),
            AltText = Text(c.AltText, $"{path}.altText", 0, MaxAltText),
        };
    }

    private static void Unit(double? v, string field)
    {
        if (v is not { } d || !(d >= 0 && d <= 1)) throw WorkException.Validation(field, "must be a number between 0 and 1.");
    }

    private static string? Text(string? value, string field, int min, int max, bool required = false, bool multiline = false)
    {
        if (value is null)
        {
            if (required) throw WorkException.Validation(field, "is required.");
            return null;
        }
        if (UnsafeText.ContainsDisallowed(value))
            throw WorkException.Validation(field, "contains characters that are not allowed.");
        var clean = TextSanitizer.Clean(value, multiline);
        var n = TextSanitizer.Length(clean);
        if (n < min || n > max)
            throw WorkException.Validation(field, min > 0 ? $"must be {min}-{max} characters." : $"allows at most {max} characters.");
        return clean;
    }
}
