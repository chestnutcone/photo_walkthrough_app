using System.Text.RegularExpressions;

namespace Vela.Api.Inquiries;

/// <summary>
/// The artist's inquiry mail (S2-3): a fixed plain-text template. The subject comes only from the fixed inquiry types, no header
/// carries user text (no Reply-To; the sender's address is shown in the body, labelled unverified), the body opens with an
/// "unverified user message" banner, and web addresses in user text are disarmed so mail clients do not turn them into links.
/// </summary>
public static partial class InquiryEmail
{
    public sealed record Message(string Subject, string TextBody);

    public const string Rule = "------------------------------------------------------------";

    public static Message Compose(string appName, string type, string senderEmail, string? workTitle, string message)
    {
        if (!InquiryValidator.Types.Contains(type)) throw new ArgumentException("Unknown inquiry type.", nameof(type));
        var about = workTitle is null ? "" : $"About your story: {Neutralise(workTitle)}\n";
        var body =
            $"{Rule}\nUNVERIFIED USER MESSAGE\n" +
            $"{appName} has not checked who sent this or what it says. Web addresses in it have been disabled.\n" +
            "Do not open links or attachments you did not expect, and never send passwords, sign-in codes or payment details by email.\n" +
            $"{Rule}\n\n" +
            $"Inquiry type: {type}\nReply to (unverified): {senderEmail}\n{about}\nMessage:\n{Neutralise(message)}\n\n" +
            $"{Rule}\nSomeone used the contact form on your {appName} profile. Your email address was not shared with them.\n";
        return new Message($"New {type} inquiry via {appName}", body);
    }

    [GeneratedRegex(@"(?i)\b(https?|ftp|mailto|javascript|data|file|tel|sms)\s*:")]
    private static partial Regex Scheme();

    [GeneratedRegex(@"(?i)\bwww\.")]
    private static partial Regex Www();

    /// <summary>A dot between a letter or digit and two or more letters: "evil.example", "a@b.example", not "3.50".</summary>
    [GeneratedRegex(@"(?<=[\p{L}\p{N}-])\.(?=\p{L}{2,})")]
    private static partial Regex DomainDot();

    /// <summary>"https://evil.example" becomes "https[:]//evil[.]example": readable, but no client links it.</summary>
    public static string Neutralise(string text)
    {
        var t = Scheme().Replace(text, m => m.Groups[1].Value + "[:]");
        t = Www().Replace(t, m => m.Value[..^1] + "[.]");
        return DomainDot().Replace(t, "[.]");
    }
}
