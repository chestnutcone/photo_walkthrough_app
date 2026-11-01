using System.Text.RegularExpressions;

namespace Vela.Api.Auth.EmailCodes;

public sealed partial class EmailCodeSettings
{
    public const string PepperSetting = "Auth:EmailCode:Pepper";
    public const int MinPepperLength = 32;

    public string Pepper { get; init; } = "";
    public bool ReviewMode { get; init; }
    public IReadOnlyList<string> ReviewEmails { get; init; } = [];
    public string ReviewCode { get; init; } = "";

    public bool IsReviewEmail(string normalizedEmail) =>
        ReviewMode && ReviewEmails.Any(e => string.Equals(e.Trim(), normalizedEmail, StringComparison.OrdinalIgnoreCase));

    /// <summary>Fails fast at startup on a missing/short pepper (outside Development) or a bad review-mode setup.</summary>
    public static EmailCodeSettings Load(IConfiguration config, IHostEnvironment env)
    {
        var pepper = config[PepperSetting] ?? "";
        if (pepper.Length < MinPepperLength && env.EnvironmentName != "Development")
            throw new InvalidOperationException(
                $"{PepperSetting} must be set to at least {MinPepperLength} characters. Set it via environment (Auth__EmailCode__Pepper) or a secret store.");

        var reviewMode = config.GetValue<bool>("Auth:ReviewMode");
        var emails = config.GetSection("Auth:ReviewEmails").Get<string[]>()?.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray() ?? [];
        var code = config["Auth:ReviewCode"] ?? "";
        if (reviewMode)
        {
            if (emails.Length == 0)
                throw new InvalidOperationException("Auth:ReviewMode is on but Auth:ReviewEmails is empty.");
            if (!SixDigits().IsMatch(code))
                throw new InvalidOperationException("Auth:ReviewMode is on but Auth:ReviewCode is missing or not exactly 6 digits.");
        }
        return new EmailCodeSettings { Pepper = pepper, ReviewMode = reviewMode, ReviewEmails = emails, ReviewCode = code };
    }

    [GeneratedRegex(@"^[0-9]{6}$")]
    private static partial Regex SixDigits();
}
