using Microsoft.Extensions.Primitives;

namespace Vela.Api.Admin;

/// <summary>Who may sign in to the admin pages. Read on every call so a change takes effect on the next request.</summary>
public interface IAdminAllowList
{
    bool Any { get; }
    bool Contains(string normalizedEmail);
}

/// <summary>Reads <c>Admin:Emails</c> (an array, or one comma-separated value) from live configuration.</summary>
public sealed class ConfigAdminAllowList(IConfiguration config) : IAdminAllowList
{
    public bool Any => Emails(config).Count > 0;
    public bool Contains(string normalizedEmail) => Emails(config).Contains(normalizedEmail);

    internal static HashSet<string> Emails(IConfiguration config)
    {
        var section = config.GetSection("Admin:Emails");
        var raw = section.GetChildren().Select(c => c.Value ?? "").ToList();
        if (raw.Count == 0) raw = (section.Value ?? "").Split(',').ToList();
        return raw.Select(Auth.EmailCodes.EmailCodeService.Normalize).OfType<string>().ToHashSet(StringComparer.Ordinal);
    }
}

/// <summary>Startup settings for the admin area.</summary>
public sealed record AdminSettings(bool Enabled, string BanPepper, TimeSpan LoginFloor = default)
{
    public const int MinPepperLength = 32;
    private const string DevPepper = "dev-only-ban-pepper-0123456789-abcdef";

    /// <summary>Every admin sign-in answer takes at least this long (default 600 ms, 0 in Development and Test), so response time does not reveal whether an address is allow-listed.</summary>
    public const int DefaultLoginFloorMs = 600;

    /// <summary>
    /// Outside Development and Test, a ban pepper of 32+ characters is required whether or not the admin pages are on
    /// (bans already recorded are matched with it; without it, or after changing it, they would silently stop applying), and an
    /// enabled admin area also needs at least one allow-listed email. Startup throws naming the setting otherwise.
    /// <c>Admin:Enabled=false</c> turns the pages off.
    /// </summary>
    public static AdminSettings Load(IConfiguration config, IHostEnvironment env)
    {
        var lenient = env.EnvironmentName is "Development" or "Test";
        var enabled = !bool.TryParse(config["Admin:Enabled"], out var flag) || flag;
        var pepper = config["Admin:BanPepper"]?.Trim() ?? "";
        var floorMs = int.TryParse(config["Admin:LoginMinimumMs"], out var ms) && ms >= 0 ? ms : (lenient ? 0 : DefaultLoginFloorMs);
        var floor = TimeSpan.FromMilliseconds(floorMs);
        if (lenient)
            return new AdminSettings(enabled, pepper.Length > 0 ? pepper : DevPepper, floor);
        if (enabled && ConfigAdminAllowList.Emails(config).Count == 0)
            throw new InvalidOperationException("Admin:Emails must list at least one admin email (or set Admin:Enabled=false).");
        if (pepper.Length < MinPepperLength)
            throw new InvalidOperationException($"Admin:BanPepper must be at least {MinPepperLength} characters. Set it via environment (Admin__BanPepper) or a secret store.");
        return new AdminSettings(enabled, pepper, floor);
    }
}
