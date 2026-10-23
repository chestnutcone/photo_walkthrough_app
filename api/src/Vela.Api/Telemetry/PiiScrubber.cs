using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Vela.Api.Telemetry;

/// <summary>Redacts personal data and credentials from free text before it reaches a log or error report.</summary>
public static partial class PiiScrubber
{
    public const string EmailMask = "[email]";
    public const string SecretMask = "[redacted]";

    [GeneratedRegex(@"[\w.%+\-']+@[\w\-]+(?:\.[\w\-]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    [GeneratedRegex(@"\bBearer\s+[\w.~+/\-]+=*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"\beyJ[\w\-]+\.[\w\-]+\.[\w\-]+", RegexOptions.CultureInvariant)]
    private static partial Regex Jwt();

    // A six digit number is only a sign-in code when it is labelled as one (code=123456, "otp":"123456").
    [GeneratedRegex(@"(\b(?:code|otp|passcode|verification_code)[""']?\s*[:=]\s*[""']?)\d{6}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Code();

    // SAS signatures and other secret-bearing key=value pairs (query strings, JSON, form bodies).
    [GeneratedRegex(@"(\b(?:sig|signature|access_token|refresh_token|id_token|api_?key|client_secret|password|secret)[""']?\s*[:=]\s*[""']?)[^&\s""',;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyedSecret();

    [return: NotNullIfNotNull(nameof(input))]
    public static string? Scrub(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var s = Bearer().Replace(input, "Bearer " + SecretMask);
        s = Jwt().Replace(s, SecretMask);
        s = KeyedSecret().Replace(s, "$1" + SecretMask);
        s = Code().Replace(s, "$1" + SecretMask);
        return Email().Replace(s, EmailMask);
    }
}
