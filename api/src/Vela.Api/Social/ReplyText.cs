using System.Buffers;
using System.Text;

namespace Vela.Api.Social;

/// <summary>Reply text rules: trimmed, 1-1000 characters (code points), no control characters except newline, valid UTF-16.</summary>
public static class ReplyText
{
    public const int MaxLength = 1000;

    /// <summary>The trimmed text, or a validation_failed error that names the field and never echoes the text.</summary>
    public static string Validate(string? raw)
    {
        if (raw is null) throw Invalid("body must be text.");
        var text = raw.Trim();
        var count = 0;
        for (var i = 0; i < text.Length;)
        {
            if (Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var used) != OperationStatus.Done)
                throw Invalid("body must be valid text.");
            if (Rune.IsControl(rune) && rune.Value != '\n') throw Invalid("body must not contain control characters other than new lines.");
            if (Common.UnsafeText.IsDisallowed(rune.Value)) throw Invalid("body must not contain text-direction control characters.");
            i += used;
            count++;
        }
        return count is >= 1 and <= MaxLength ? text : throw Invalid($"body must be 1 to {MaxLength} characters.");
    }

    private static SafetyException Invalid(string message) => new(StatusCodes.Status400BadRequest, "validation_failed", message);
}
