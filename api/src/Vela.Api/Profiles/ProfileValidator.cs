using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Vela.Api.Profiles;

/// <summary>Field limits for profile text. Messages name the field and never echo user text.</summary>
public static partial class ProfileValidator
{
    public const int MaxFollowIds = 50;
    private const int MaxServices = 10, MaxShows = 20;

    [GeneratedRegex(@"^[0-9]{4}\z", RegexOptions.CultureInvariant)]
    private static partial Regex YearPattern();

    public static string DisplayName(string? value)
    {
        var name = (value ?? "").Trim();
        Length("displayName", name, 1, 60);
        return name;
    }

    public static void ValidateUpdate(UpdateProfileRequest r)
    {
        if (r.DisplayName is not null) DisplayName(r.DisplayName);
        Optional("bio", r.Bio, 280, allowControl: false);
        Optional("city", r.City, 80);
        Optional("discipline", r.Discipline, 60);
        Optional("availability", r.Availability, 60);
        Optional("social", r.Social, 60);
        Optional("studio", r.Studio, 120);
        Optional("replyTime", r.ReplyTime, 120);
        Website(r.Website);
        PublicEmail(r.PublicEmail);
        Services(r.Services);
        Shows(r.Shows);
    }

    private static void Optional(string field, string? v, int max, bool allowControl = true)
    {
        if (v is null) return;
        if (Count(v) > max) throw ProfileException.Validation(field, $"must be at most {max} characters.");
        if (!allowControl && HasControl(v)) throw ProfileException.Validation(field, "contains invalid characters.");
    }

    private static void Length(string field, string v, int min, int max)
    {
        var n = Count(v);
        if (n < min || n > max) throw ProfileException.Validation(field, $"must be {min}-{max} characters.");
    }

    private static void Website(string? v)
    {
        if (string.IsNullOrEmpty(v)) return;
        Optional("website", v, 200);
        if (!Uri.TryCreate(v, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host))
            throw ProfileException.Validation("website", "must be an http or https address.");
    }

    private static void PublicEmail(string? v)
    {
        if (v is null) return;
        var valid = v.Length <= 254 && v.All(c => !char.IsWhiteSpace(c) && !char.IsControl(c))
            && MailAddress.TryCreate(v, out var a) && a.Address == v && a.Host.Contains('.') && !a.Host.EndsWith('.');
        if (!valid) throw ProfileException.Validation("publicEmail", "must be a valid email address.");
    }

    private static void Services(IReadOnlyList<ServiceDto>? list)
    {
        if (list is null) return;
        if (list.Count > MaxServices) throw ProfileException.Validation("services", $"allows at most {MaxServices} items.");
        foreach (var s in list)
        {
            Length("services title", s.Title, 1, 80);
            Optional("services description", s.Description, 300, allowControl: false);
            Optional("services price", s.Price, 40);
        }
    }

    private static void Shows(IReadOnlyList<ShowDto>? list)
    {
        if (list is null) return;
        if (list.Count > MaxShows) throw ProfileException.Validation("shows", $"allows at most {MaxShows} items.");
        foreach (var s in list)
        {
            if (!YearPattern().IsMatch(s.Year)) throw ProfileException.Validation("shows year", "must be 4 digits.");
            Optional("shows text", s.Text, 200);
        }
    }

    /// <summary>Length in code points so an emoji counts as one character.</summary>
    private static int Count(string v) => v.EnumerateRunes().Count();

    private static bool HasControl(string v) => v.Any(c => char.IsControl(c) && c != '\n');
}
