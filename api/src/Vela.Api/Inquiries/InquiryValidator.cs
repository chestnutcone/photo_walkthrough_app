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

    private static string ValidEmail(string? raw) =>
        TryEmail(raw, out var email) ? email : throw Invalid("email must be a valid email address of at most 254 characters.");

    /// <summary>The trimmed address when it is plain and valid (no spaces, control characters or header-breaking symbols).</summary>
    public static bool TryEmail(string? raw, out string email)
    {
        email = raw?.Trim() ?? "";
        return email.Length is > 0 and <= MaxEmailLength && email.All(c => c > ' ' && c != '\u007f' && !"<>(),;:\"[]\\".Contains(c))
            && EmailPattern().IsMatch(email);
    }

    private static string ValidMessage(string? raw)
    {
        if (raw is null) throw Invalid("message must be text.");
        if (Common.UnsafeText.ContainsDisallowed(raw)) throw Invalid("message must not contain text-direction control characters.");
        var text = Common.TextSanitizer.Clean(raw, multiline: true); // S2-2
        return Common.TextSanitizer.Length(text) is >= 1 and <= MaxMessageLength ? text : throw Invalid($"message must be 1 to {MaxMessageLength} characters.");
    }

    public static SafetyException Invalid(string message) => new(StatusCodes.Status400BadRequest, "validation_failed", message);
}
