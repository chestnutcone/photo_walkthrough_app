namespace Vela.Api.Common;

/// <summary>
/// Launch blockers (FINALAPI M9). Outside Development and Test the API refuses to start without a registered DMCA agent (DMCA
/// safe harbor) and Cloudflare purge settings (otherwise deleted and taken-down images stay cached at the edge, contradicting
/// the deletion statement). A non-production Azure environment (it also runs as Production) opts out with
/// <c>Launch:RequireLaunchSettings=false</c>; infra sets it to true only for <c>environment=prod</c>.
/// </summary>
public static class LaunchSettings
{
    public const string Switch = "Launch:RequireLaunchSettings";

    private static readonly string[] Required =
        ["Legal:DmcaAgentName", "Legal:DmcaAgentEmail", "Legal:DmcaAgentAddress", "Cloudflare:ZoneId", "Cloudflare:ApiToken"];

    public static void Verify(IConfiguration config, IHostEnvironment env)
    {
        if (env.IsDevelopment() || env.IsEnvironment("Test")) return;
        if (!config.GetValue(Switch, true)) return;
        var missing = Required.Where(k => string.IsNullOrWhiteSpace(config[k])).ToList();
        if (missing.Count == 0) return;
        throw new InvalidOperationException(
            $"Start-up refused: launch-blocking settings are missing: {string.Join(", ", missing)}. Set them (docs/deploy.md, " +
            $"production checklist), or for a non-production deployment set {Switch.Replace(":", "__")}=false.");
    }
}
