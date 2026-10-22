using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;
using Vela.Api.Social;

namespace Vela.Api.Inquiries;

/// <summary>Request rules for inquiries. Errors name the field and never echo what the user typed.</summary>
public static partial class InquiryValidator
{
    public const int MaxEmailLength = 254;
    public const int MaxMessageLength = 2000;
    public static readonly string[] Types = ["Commission", "Buy a print", "Licensing", "Collaboration", "Just saying hi"];

    [GeneratedRegex(@"^[^@]{1,64}@[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)+$")]
    private static partial Regex EmailPattern();

    public static InquiryRequest Validate(InquiryRequest req) =>
        req with { Type = ValidType(req.Type), Email = ValidEmail(req.Email), Message = ValidMessage(req.Message) };

    private static string ValidType(string? type) =>
        type is not null && Types.Contains(type) ? type : throw Invalid("type must be one of the inquiry types.");

    private static string ValidEmail(string? raw)
    {
        var email = raw?.Trim() ?? "";
        var ok = email.Length is > 0 and <= MaxEmailLength && email.All(c => c > ' ' && c != '\u007f' && !"<>(),;:\"[]\\".Contains(c))
            && EmailPattern().IsMatch(email);
        return ok ? email : throw Invalid("email must be a valid email address of at most 254 characters.");
    }

    private static string ValidMessage(string? raw)
    {
        if (raw is null) throw Invalid("message must be text.");
        var text = raw.Trim();
        var count = 0;
        for (var i = 0; i < text.Length;)
        {
            if (Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var used) != OperationStatus.Done)
                throw Invalid("message must be valid text.");
            if (Rune.IsControl(rune) && rune.Value != '\n') throw Invalid("message must not contain control characters other than new lines.");
            i += used;
            count++;
        }
        return count is >= 1 and <= MaxMessageLength ? text : throw Invalid($"message must be 1 to {MaxMessageLength} characters.");
    }

    public static SafetyException Invalid(string message) => new(StatusCodes.Status400BadRequest, "validation_failed", message);
}
