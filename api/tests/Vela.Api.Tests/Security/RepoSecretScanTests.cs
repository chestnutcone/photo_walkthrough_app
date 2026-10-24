using System.Text.RegularExpressions;

namespace Vela.Api.Tests.Security;

/// <summary>
/// Scans the repository's configuration, infrastructure, workflow, container and script files for secret values. Documented
/// development-only values are allow-listed below, each with the reason it is safe; anything else fails the build.
/// The same rules run in CI through gitleaks (.gitleaks.toml carries the same allow-list).
/// </summary>
public partial class RepoSecretScanTests
{
    private sealed record Allowed(string File, string Value, string Reason);

    /// <summary>Every literal that looks like a secret but is a documented dev-only value. Keep in step with .gitleaks.toml.</summary>
    private static readonly Allowed[] AllowList =
    [
        new("api/src/Vela.Api/appsettings.Development.json", "dev-only-email-code-pepper-0123456789-abcdef", "Development-only pepper; non-Development startup refuses to run without a real one"),
        new("api/src/Vela.Api/appsettings.Development.json", "dev-only-signing-key-do-not-use-in-production-0123456789", "Development-only JWT key; Program.cs requires Auth:Jwt:SigningKey from the environment elsewhere"),
        new("api/src/Vela.Api/appsettings.Development.json", "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==", "Azurite's published well-known dev account key (documented by Microsoft), only valid against the local emulator"),
        new("api/src/Vela.Api/appsettings.Development.json", "vela_dev_only", "Local docker-compose Postgres password"),
        new("docker-compose.yml", "vela_dev_only", "Local docker-compose Postgres password; the port is only published for local development"),
    ];

    private static readonly string[] SkipDirs = ["bin", "obj", ".git", ".claude", ".dart_tool", "build", "node_modules", ".superpowers", "TestResults", "design_handoff_vela_app", "Pods", ".gradle", "Migrations", "ephemeral", ".symlinks", ".plugin_symlinks"];

    // Scanned with the full rule set (key names plus high-confidence patterns).
    private static bool IsConfigLike(string rel) =>
        Regex.IsMatch(rel, @"(^|/)appsettings[^/]*\.json$") || rel.StartsWith("infra/", StringComparison.Ordinal) || rel.StartsWith(".github/", StringComparison.Ordinal)
        || Regex.IsMatch(rel, @"(^|/)(docker-compose[^/]*\.ya?ml|Dockerfile[^/]*|\.dockerignore)$") || rel.StartsWith("scripts/", StringComparison.Ordinal)
        || Regex.IsMatch(rel, @"\.(bicepparam|parameters\.json|toml|env|properties|plist|gradle|kts)$") || rel.EndsWith("pubspec.yaml", StringComparison.Ordinal)
        || rel.StartsWith("tools/", StringComparison.Ordinal);

    // Source files get the high-confidence patterns only (key-name rules would match constant names).
    private static bool IsSource(string rel) => Regex.IsMatch(rel, @"\.(cs|cshtml|dart|html|js|json|yml|yaml|md)$")
        && !rel.StartsWith("api/tests/", StringComparison.Ordinal) && !rel.Contains("/test/", StringComparison.Ordinal)
        && !rel.StartsWith("app/packages/vela_api_client/", StringComparison.Ordinal);

    private static readonly (string Name, Regex Pattern)[] HighConfidence =
    [
        ("private key block", new(@"-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP |ENCRYPTED )?PRIVATE KEY", RegexOptions.Compiled)),
        ("JWT", new(@"\beyJ[A-Za-z0-9_\-]{10,}\.eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}", RegexOptions.Compiled)),
        ("storage account key", new(@"AccountKey=[A-Za-z0-9+/]{40,}={0,2}", RegexOptions.Compiled)),
        ("ACS access key", new(@"accesskey=[A-Za-z0-9+/]{30,}={0,2}", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("SAS signature", new(@"[?&;]sig=[A-Za-z0-9%+/]{30,}", RegexOptions.Compiled)),
        ("connection string password", new(@"(?i)\bPassword=(?![\s;$'""{<])[^;""'\s]+", RegexOptions.Compiled)),
        ("AWS access key id", new(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b", RegexOptions.Compiled)),
        ("GitHub token", new(@"\bgh[pousr]_[A-Za-z0-9]{30,}\b", RegexOptions.Compiled)),
        ("Slack token", new(@"\bxox[abprs]-[A-Za-z0-9-]{10,}", RegexOptions.Compiled)),
        ("Google API key", new(@"\bAIza[0-9A-Za-z_\-]{35}\b", RegexOptions.Compiled)),
        ("Sentry DSN with key", new(@"https://[0-9a-f]{32}@[\w.\-]+/\d+", RegexOptions.Compiled)),
        ("Stripe live key", new(@"\b[sr]k_live_[0-9A-Za-z]{16,}", RegexOptions.Compiled)),
        ("Cloudflare/API bearer literal", new(@"(?i)authorization:\s*bearer\s+[A-Za-z0-9_\-\.]{30,}", RegexOptions.Compiled)),
    ];

    // key = "literal" (or the parameter-file form key: { "value": "literal" }) where the key name says it is secret.
    private static readonly Regex NamedSecret = new(
        @"[""']?(?<key>[A-Za-z0-9_.\-]*(?:password|passwd|secret|signingkey|pepper|accountkey|connectionstring|apikey|privatekey|decryptionkey|verificationkey|sastoken|accesskey)[A-Za-z0-9_.\-]*)[""']?\s*[:=]\s*(?:\{\s*""value""\s*:\s*)?[""'](?<value>[^""'\r\n]{8,})[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Keys that name or point at a secret rather than hold one (Container Apps secretRef, role-definition ids, vault names).
    private static readonly Regex ReferenceKey = new(@"(?i)(ref|name|uri|url|user|role|id)$", RegexOptions.Compiled);

    private static readonly string[] PlaceholderMarkers =
        ["${", "$(", "{{", "<", "secrets.", "vars.", "env.", "reference(", "listKeys", "secretRef", "keyvault", "***", "REPLACE", "your-", "changeme", "example", "@secure", "getSecret"];

    private static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static IEnumerable<string> RepoFiles(string root)
    {
        var stack = new Stack<string>([root]);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var d in Directory.GetDirectories(dir))
                if (!SkipDirs.Contains(Path.GetFileName(d))) stack.Push(d);
            foreach (var f in Directory.GetFiles(dir))
            {
                var info = new FileInfo(f);
                if (info.Length is > 0 and < 1_000_000 && !IsBinary(f)) yield return f;
            }
        }
    }

    private static bool IsBinary(string path) =>
        Regex.IsMatch(path, @"\.(png|jpe?g|gif|webp|ico|ttf|otf|woff2?|jar|zip|gz|keystore|jks|pdf|dll|so|a|class|bin|webm|mp4|icns)$", RegexOptions.IgnoreCase);

    private static bool IsAllowed(string rel, string text) =>
        AllowList.Any(a => a.File == rel && text.Contains(a.Value, StringComparison.Ordinal));

    internal static List<string> Scan(string root)
    {
        var findings = new List<string>();
        foreach (var file in RepoFiles(root))
        {
            var rel = Rel(root, file);
            var config = IsConfigLike(rel);
            if (!config && !IsSource(rel)) continue;
            var lineNo = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNo++;
                foreach (var (name, pattern) in HighConfidence)
                    foreach (Match m in pattern.Matches(line))
                        if (!IsAllowed(rel, m.Value) && !IsAllowed(rel, line)) findings.Add($"{rel}:{lineNo} {name}");
                if (!config) continue;
                foreach (Match m in NamedSecret.Matches(line))
                {
                    var value = m.Groups["value"].Value;
                    if (ReferenceKey.IsMatch(m.Groups["key"].Value)) continue;
                    if (PlaceholderMarkers.Any(p => value.Contains(p, StringComparison.OrdinalIgnoreCase))) continue;
                    if (IsAllowed(rel, value)) continue;
                    findings.Add($"{rel}:{lineNo} secret-named setting '{m.Groups["key"].Value}' holds a literal value");
                }
            }
        }
        return findings.Distinct().ToList();
    }

    [Fact]
    public void No_secret_values_in_repo_config_files()
    {
        var findings = Scan(SecurityContract.RepoRoot());
        Assert.True(findings.Count == 0, "Secret-looking values found (allow-list documented dev values in RepoSecretScanTests and .gitleaks.toml):\n" + string.Join("\n", findings));
    }

    [Fact]
    public void The_scanner_catches_planted_secrets_and_ignores_placeholders()
    {
        var dir = Directory.CreateTempSubdirectory("secretscan").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "infra"));
            Directory.CreateDirectory(Path.Combine(dir, ".github", "workflows"));
            File.WriteAllText(Path.Combine(dir, "infra", "parameters.prod.json"), "{ \"adminPassword\": { \"value\": \"Sup3rSecretValue!\" }, \"ok\": { \"value\": \"${KV_REF}\" } }");
            File.WriteAllText(Path.Combine(dir, ".github", "workflows", "x.yml"), "env:\n  KEY: ${{ secrets.KEY }}\n  password: \"${{ secrets.PW }}\"\n  token: gh" + "p_abcdefghijklmnopqrstuvwxyz0123456789\n");
            File.WriteAllText(Path.Combine(dir, "appsettings.json"), "{ \"Db\": \"Host=h;Password=hunter2hunter2\", \"Jwt\": \"" + "eyJhbGciOiJIUzI1NiJ9" + ".eyJzdWIiOiIxMjM0NTY3ODkwIn0" + ".dBjftJeZ4CVPmB92K27uhbUJU1p1r_wW1gFWFOEjXk\" }");
            File.WriteAllText(Path.Combine(dir, "infra", "id_key.toml"), "-----BEGIN RSA PRIVATE " + "KEY-----\nabc\n");

            var found = Scan(dir);

            Assert.Contains(found, f => f.Contains("parameters.prod.json") && f.Contains("adminPassword"));
            Assert.Contains(found, f => f.Contains("x.yml") && f.Contains("GitHub token"));
            Assert.Contains(found, f => f.Contains("appsettings.json") && f.Contains("connection string password"));
            Assert.Contains(found, f => f.Contains("appsettings.json") && f.Contains("JWT"));
            Assert.Contains(found, f => f.Contains("id_key.toml") && f.Contains("private key"));
            Assert.DoesNotContain(found, f => f.Contains("KV_REF") || f.Contains("secrets.KEY") || f.Contains("secrets.PW"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Key_material_files_are_not_in_the_repository_and_are_ignored_by_git()
    {
        var root = SecurityContract.RepoRoot();
        var forbidden = RepoFiles(root).Select(f => Rel(root, f))
            .Where(r => Regex.IsMatch(r, @"(^|/)(\.env(\.(?!example$).+)?|id_rsa|id_ed25519|key\.properties|google-services\.json|GoogleService-Info\.plist|local\.settings\.json)$|\.(pem|p12|pfx|jks|keystore|publishsettings)$"))
            .ToList();
        Assert.Empty(forbidden);

        var ignore = File.ReadAllText(Path.Combine(root, ".gitignore"));
        foreach (var pattern in new[] { ".env", "*.pem", "*.p12", "*.pfx", "*.jks", "*.keystore", "key.properties", "google-services.json", "GoogleService-Info.plist" })
            Assert.Contains(pattern, ignore);
    }

    [Fact]
    public void Every_allow_list_entry_is_still_used_so_stale_exceptions_are_removed()
    {
        var root = SecurityContract.RepoRoot();
        foreach (var a in AllowList)
        {
            var path = Path.Combine(root, a.File);
            Assert.True(File.Exists(path), $"allow-listed file is gone: {a.File}");
            Assert.Contains(a.Value, File.ReadAllText(path));
            Assert.False(string.IsNullOrWhiteSpace(a.Reason));
        }
    }
}
