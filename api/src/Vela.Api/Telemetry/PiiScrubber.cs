using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Vela.Api.Telemetry;

/// <summary>Redacts personal data and credentials from free text before it reaches a log or error report.</summary>
public static partial class PiiScrubber
{
    private const int TimeoutMs = 250;
    public const string EmailMask = "[email]";
    public const string TimeoutMask = "[redacted: unscrubbable]";
    public const string SecretMask = "[redacted]";

    [GeneratedRegex(@"[\w.%+\-']+@[\w\-]+(?:\.[\w\-]+)+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex Email();

    [GeneratedRegex(@"\bBearer\s+[\w.~+/\-]+=*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"\beyJ[\w\-]+\.[\w\-]+\.[\w\-]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex Jwt();

    // A six digit number is only a sign-in code when it is labelled as one (code=123456, "otp":"123456").
    [GeneratedRegex(@"(\b(?:code|otp|passcode|verification_code)[""']?\s*[:=]\s*[""']?)\d{6}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex Code();

    // SAS signatures and other secret-bearing key=value pairs (query strings, JSON, form bodies).
    [GeneratedRegex(@"(\b(?:sig|signature|access_token|refresh_token|id_token|api_?key|client_secret|password|secret)[""']?\s*[:=]\s*[""']?)[^&\s""',;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex KeyedSecret();

    // Postgres error detail repeats the offending row values: Key (email)=(jane@example.com) already exists.
    [GeneratedRegex(@"\bKey \([^)]*\)=\([^)]*\)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex PostgresKeyDetail();

    [return: NotNullIfNotNull(nameof(input))]
    public static string? Scrub(string? input) => ScrubGuarded(input, ScrubCore);

    /// <summary>Fail closed: if scrubbing cannot finish in time, nothing of the input is kept.</summary>
    [return: NotNullIfNotNull(nameof(input))]
    internal static string? ScrubGuarded(string? input, Func<string, string> core)
    {
        if (string.IsNullOrEmpty(input)) return input;
        try { return core(input); }
        catch (RegexMatchTimeoutException) { return TimeoutMask; }
    }

    private static string ScrubCore(string input)
    {
        var s = PostgresKeyDetail().Replace(input, "Key " + SecretMask);
        s = Bearer().Replace(s, "Bearer " + SecretMask);
        s = Jwt().Replace(s, SecretMask);
        s = KeyedSecret().Replace(s, "$1" + SecretMask);
        s = Code().Replace(s, "$1" + SecretMask);
        return Email().Replace(s, EmailMask);
    }
}
