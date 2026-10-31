using Vela.Api.Common;

namespace Vela.Api.Social;

/// <summary>Reply text rules: cleaned by <see cref="TextSanitizer"/> (multi-line, S2-2), then 1-1000 code points; bidi controls refused.</summary>
public static class ReplyText
{
    public const int MaxLength = 1000;

    /// <summary>The cleaned text, or a validation_failed error that names the field and never echoes the text.</summary>
    public static string Validate(string? raw)
    {
        if (raw is null) throw Invalid("body must be text.");
        if (UnsafeText.ContainsDisallowed(raw)) throw Invalid("body must not contain text-direction control characters.");
        var text = TextSanitizer.Clean(raw, multiline: true);
        return TextSanitizer.Length(text) is >= 1 and <= MaxLength ? text : throw Invalid($"body must be 1 to {MaxLength} characters.");
    }

    private static SafetyException Invalid(string message) => new(StatusCodes.Status400BadRequest, "validation_failed", message);
}
