using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Vela.Api.Profiles;

/// <summary>
/// Field limits for profile text. Every text field is cleaned by <see cref="Common.TextSanitizer"/> first (S2-2) and the limits
/// apply to the cleaned text in code points; bidi controls are still refused outright. Messages name the field and never echo user text.
/// </summary>
public static partial class ProfileValidator
{
    public const int MaxFollowIds = 50;
    private const int MaxServices = 10, MaxShows = 20;

    [GeneratedRegex(@"^[0-9]{4}\z", RegexOptions.CultureInvariant)]
    private static partial Regex YearPattern();

    public static string DisplayName(string? value)
    {
        var name = Clean("displayName", value ?? "", multiline: false);
        Length("displayName", name, 1, 60);
        return name;
    }

    /// <summary>The request with every text field cleaned, or a validation error.</summary>
    public static UpdateProfileRequest ValidateUpdate(UpdateProfileRequest r)
    {
        var cleaned = r with
        {
            DisplayName = r.DisplayName is null ? null : DisplayName(r.DisplayName),
            Bio = Optional("bio", r.Bio, 280, multiline: true),
            City = Optional("city", r.City, 80),
            Discipline = Optional("discipline", r.Discipline, 60),
            Availability = Optional("availability", r.Availability, 60),
            Social = Optional("social", r.Social, 60),
            Studio = Optional("studio", r.Studio, 120),
            ReplyTime = Optional("replyTime", r.ReplyTime, 120),
            Website = Website(r.Website),
            Services = Services(r.Services),
            Shows = Shows(r.Shows),
        };
        PublicEmail(r.PublicEmail);
        return cleaned;
    }

    private static string Clean(string field, string v, bool multiline)
    {
        if (Common.UnsafeText.ContainsDisallowed(v)) throw ProfileException.Validation(field, "contains invalid characters.");
        return Common.TextSanitizer.Clean(v, multiline);
    }

    private static string? Optional(string field, string? v, int max, bool multiline = false)
    {
        if (v is null) return null;
        var clean = Clean(field, v, multiline);
        if (Count(clean) > max) throw ProfileException.Validation(field, $"must be at most {max} characters.");
        return clean;
    }

    private static void Length(string field, string v, int min, int max)
    {
        var n = Count(v);
        if (n < min || n > max) throw ProfileException.Validation(field, $"must be {min}-{max} characters.");
    }

    private static string? Website(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        var v = Optional("website", raw, 200)!;
        if (v.Length == 0) return v;
        if (!Uri.TryCreate(v, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host))
            throw ProfileException.Validation("website", "must be an http or https address.");
        return v;
    }

    private static void PublicEmail(string? v)
    {
        if (v is null) return;
        var valid = v.Length <= 254 && v.All(c => !char.IsWhiteSpace(c) && !char.IsControl(c))
            && MailAddress.TryCreate(v, out var a) && a.Address == v && a.Host.Contains('.') && !a.Host.EndsWith('.');
        if (!valid) throw ProfileException.Validation("publicEmail", "must be a valid email address.");
    }

    private static IReadOnlyList<ServiceDto>? Services(IReadOnlyList<ServiceDto>? list)
    {
        if (list is null) return null;
        if (list.Count > MaxServices) throw ProfileException.Validation("services", $"allows at most {MaxServices} items.");
        return list.Select(s =>
        {
            var title = Clean("services title", s.Title, multiline: false);
            Length("services title", title, 1, 80);
            return new ServiceDto(title, Optional("services description", s.Description, 300, multiline: true)!, Optional("services price", s.Price, 40)!);
        }).ToList();
    }

    private static IReadOnlyList<ShowDto>? Shows(IReadOnlyList<ShowDto>? list)
    {
        if (list is null) return null;
        if (list.Count > MaxShows) throw ProfileException.Validation("shows", $"allows at most {MaxShows} items.");
        return list.Select(s =>
        {
            if (!YearPattern().IsMatch(s.Year)) throw ProfileException.Validation("shows year", "must be 4 digits.");
            return new ShowDto(s.Year, Optional("shows text", s.Text, 200)!);
        }).ToList();
    }

    /// <summary>Length in code points of the cleaned text, exactly as the app counts it.</summary>
    private static int Count(string v) => Common.TextSanitizer.Length(v);
}
