using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vela.Api.Inquiries;
using Vela.Api.Social;

namespace Vela.Api.Legal;

public sealed record DmcaRequest(
    string ClaimantName, string ClaimantEmail, string ClaimantAddress, string? ClaimantPhone, string WorkReference,
    string OriginalWorkDescription, string SignatureName);

/// <summary>Request rules for DMCA notices. Errors name the field and never echo what was typed. Text is kept verbatim.</summary>
public static partial class DmcaValidator
{
    public const int MaxBodyBytes = 20 * 1024;

    public static DmcaRequest Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw Invalid("body", "must be a JSON object");
        var name = Text(root, "claimantName", 1, 120, multiline: false);
        var email = Email(root);
        var address = Text(root, "claimantAddress", 1, 300, multiline: true);
        var phone = OptionalText(root, "claimantPhone", 40);
        var reference = Text(root, "workReference", 1, 500, multiline: false);
        var description = Text(root, "originalWorkDescription", 1, 2000, multiline: true);
        MustBeTrue(root, "goodFaithStatement");
        MustBeTrue(root, "perjuryStatement");
        var signature = Text(root, "signatureName", 1, 120, multiline: false);
        return new DmcaRequest(name, email, address, phone, reference, description, signature);
    }

    /// <summary>1 to <paramref name="max"/> characters (not blank), no control characters; new lines only where allowed.</summary>
    public static string Text(JsonElement root, string field, int min, int max, bool multiline)
    {
        if (!root.TryGetProperty(field, out var v) || v.ValueKind != JsonValueKind.String) throw Invalid(field, "must be text");
        return Check(v.GetString()!, field, min, max, multiline);
    }

    public static string? OptionalText(JsonElement root, string field, int max)
    {
        if (!root.TryGetProperty(field, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.String) throw Invalid(field, "must be text");
        var text = v.GetString()!;
        return text.Length == 0 ? null : Check(text, field, 1, max, multiline: false);
    }

    public static string Check(string text, string field, int min, int max, bool multiline)
    {
        var count = 0;
        for (var i = 0; i < text.Length;)
        {
            if (Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var used) != OperationStatus.Done) throw Invalid(field, "must be valid text");
            if (Rune.IsControl(rune) && !(multiline && rune.Value == '\n')) throw Invalid(field, "must not contain control characters");
            if (Common.UnsafeText.IsDisallowed(rune.Value)) throw Invalid(field, "must not contain text-direction control characters");
            i += used;
            count++;
        }
        if (count < min || count > max || text.Trim().Length == 0) throw Invalid(field, $"must be {min} to {max} characters");
        return text;
    }

    public static void MustBeTrue(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var v) || v.ValueKind != JsonValueKind.True) throw Invalid(field, "must be true");
    }

    private static string Email(JsonElement root)
    {
        if (!root.TryGetProperty("claimantEmail", out var v) || v.ValueKind != JsonValueKind.String) throw Invalid("claimantEmail", "must be a valid email address");
        return InquiryValidator.TryEmail(v.GetString(), out var email) ? email : throw Invalid("claimantEmail", "must be a valid email address of at most 254 characters");
    }

    /// <summary>Only a GUID or a story link <c>.../s/{guid}</c>; null when the text names no story.</summary>
    public static Guid? WorkIdFrom(string reference)
    {
        var text = reference.Trim();
        if (Guid.TryParse(text, out var bare)) return bare;
        var m = StoryLink().Match(text);
        return m.Success && Guid.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }

    [GeneratedRegex(@"(?:^|/)s/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})(?=$|[/?#\s])")]
    private static partial Regex StoryLink();

    public static SafetyException Invalid(string field, string problem) =>
        new(StatusCodes.Status400BadRequest, "validation_failed", $"{field} {problem}.");
}
