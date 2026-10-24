namespace Vela.Api.Works;

/// <summary>Request limits for updateWork. Text is validated, never rewritten; messages name the field only.</summary>
public static class WorkValidator
{
    public const int MaxTitle = 200, MaxIntro = 1000, MaxAltText = 300, MaxChapters = 40;
    public const int MaxChapterTitle = 120, MaxChapterBody = 1000, MaxChapterMeta = 80;
    public const double MinZoom = 1.0, MaxZoom = 8.0;

    public static void Validate(UpdateWorkRequest? req)
    {
        if (req is null) throw WorkException.Validation("body", "must be a JSON object.");
        Text(req.Title, "title", 1, MaxTitle, required: true);
        Text(req.Intro, "intro", 0, MaxIntro);
        Text(req.AltText, "altText", 0, MaxAltText);
        if (req.Chapters is null) throw WorkException.Validation("chapters", "is required.");
        if (req.Chapters.Count > MaxChapters) throw WorkException.Validation("chapters", $"allows at most {MaxChapters} items.");
        for (var i = 0; i < req.Chapters.Count; i++) Chapter(req.Chapters[i], $"chapters[{i}]");
    }

    private static void Chapter(ChapterInput? c, string path)
    {
        if (c is null) throw WorkException.Validation(path, "must be an object.");
        Unit(c.X, $"{path}.x");
        Unit(c.Y, $"{path}.y");
        if (c.Zoom is { } z && !(z >= MinZoom && z <= MaxZoom))
            throw WorkException.Validation($"{path}.zoom", $"must be null or between {MinZoom:0.0} and {MaxZoom:0.0}.");
        Text(c.Title, $"{path}.title", 1, MaxChapterTitle, required: true);
        Text(c.Body, $"{path}.body", 0, MaxChapterBody);
        Text(c.Meta, $"{path}.meta", 0, MaxChapterMeta);
        Text(c.AltText, $"{path}.altText", 0, MaxAltText);
    }

    private static void Unit(double? v, string field)
    {
        if (v is not { } d || !(d >= 0 && d <= 1)) throw WorkException.Validation(field, "must be a number between 0 and 1.");
    }

    private static void Text(string? value, string field, int min, int max, bool required = false)
    {
        if (value is null)
        {
            if (required) throw WorkException.Validation(field, "is required.");
            return;
        }
        if (value.Length < min || value.Length > max || (required && string.IsNullOrWhiteSpace(value)))
            throw WorkException.Validation(field, min > 0 ? $"must be {min}-{max} characters." : $"allows at most {max} characters.");
        if (Common.UnsafeText.ContainsDisallowed(value))
            throw WorkException.Validation(field, "contains characters that are not allowed.");
        if (value.Any(ch => char.IsControl(ch) && ch is not ('\n' or '\r')))
            throw WorkException.Validation(field, "contains characters that are not allowed.");
    }
}
