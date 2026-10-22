using System.Text.RegularExpressions;

namespace Vela.Api.Profiles;

public static partial class HandleRules
{
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "admin", "administrator", "support", "help", "vela", "moderator", "mod", "staff", "system",
        "root", "api", "www", "null", "undefined", "me", "artists", "works", "search", "notifications",
    };

    // ASCII classes only, anchored with \z so a trailing newline can never slip through.
    [GeneratedRegex(@"^[a-z0-9_]{3,20}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static string Normalize(string handle) => (handle ?? "").Trim().ToLowerInvariant();

    public static bool IsValid(string handle)
    {
        if (handle is null) return false;
        var n = Normalize(handle);
        return Pattern().IsMatch(n) && !Reserved.Contains(n);
    }
}
