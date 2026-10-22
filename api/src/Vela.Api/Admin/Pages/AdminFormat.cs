namespace Vela.Api.Admin.Pages;

/// <summary>Display helpers shared by the admin pages.</summary>
public static class AdminFormat
{
    public static string Age(DateTimeOffset since, DateTimeOffset now)
    {
        var span = now - since;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        return $"{(int)span.TotalDays}d {span.Hours}h";
    }

    public static string When(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"auto:unsafe_text:6" becomes "auto: unsafe_text (6)"; plain reasons pass through.</summary>
    public static string Reason(string reason)
    {
        var parts = reason.Split(':');
        return parts is ["auto", var category, var severity] ? $"auto: {category} ({severity})" : reason;
    }
}
