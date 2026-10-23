using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Vela.Api.Abuse;

public sealed class PlayIntegrityOptions
{
    public const string Section = "Abuse:PlayIntegrity";
    /// <summary>Android applicationId the integrity token must be minted for.</summary>
    public string PackageName { get; set; } = "";
    /// <summary>Base64 AES-256 key from Play Console (response encryption, "Google-managed" keys are not used).</summary>
    public string DecryptionKey { get; set; } = "";
    /// <summary>Base64 X.509 SubjectPublicKeyInfo of the Play Console EC verification key.</summary>
    public string VerificationKey { get; set; } = "";
    /// <summary>Optional allow-list of signing-certificate SHA-256 digests (base64url). Empty = any Play-recognised build.</summary>
    public string[] AllowedCertificateDigests { get; set; } = [];

    public bool IsConfigured => PackageName.Length > 0 && DecryptionKey.Length > 0 && VerificationKey.Length > 0;

    public void Validate()
    {
        if (!IsConfigured)
            throw new InvalidOperationException($"{Section}:PackageName, {Section}:DecryptionKey and {Section}:VerificationKey must all be set.");
        try
        {
            if (Convert.FromBase64String(DecryptionKey).Length != 32) throw new FormatException("the decryption key must be 32 bytes");
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(VerificationKey), out _);
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            throw new InvalidOperationException($"{Section} holds an invalid key: {e.Message}", e);
        }
    }
}

public sealed class AppAttestOptions
{
    public const string Section = "Abuse:AppAttest";
    /// <summary>"TEAMID.bundle.id" the attestation must be bound to.</summary>
    public string AppId { get; set; } = "";
    /// <summary>PEM of the trust anchor; empty = Apple's published App Attestation Root CA.</summary>
    public string RootCertificatePem { get; set; } = "";
    /// <summary>Accept the development-environment AAGUID (sandbox builds). Never in production.</summary>
    public bool AllowDevelopment { get; set; }

    public bool IsConfigured => AppId.Length > 0;

    public string EffectiveRootPem => RootCertificatePem.Length > 0 ? RootCertificatePem : AppleRoots.AppAttestationRootPem;

    public void Validate()
    {
        if (!IsConfigured) throw new InvalidOperationException($"{Section}:AppId must be set (TEAMID.bundle.id).");
        try { using var _ = X509Certificate2.CreateFromPem(EffectiveRootPem); }
        catch (CryptographicException e) { throw new InvalidOperationException($"{Section}:RootCertificatePem is not a valid certificate.", e); }
    }
}

/// <summary>Abuse and bot protection settings (section "Abuse").</summary>
public sealed class AbuseOptions
{
    public const int DefaultSignupsPerIpPerDay = 5;
    private const int TestSignupsPerIpPerDay = 100_000;

    public bool RequireAttestation { get; init; }
    public IReadOnlyList<string> CloudflareRanges { get; init; } = Abuse.CloudflareRanges.Default;
    public IReadOnlyList<string> DisposableEmailDomains { get; init; } = [];
    public int SignupsPerIpPerDay { get; init; } = DefaultSignupsPerIpPerDay;
    public IReadOnlyDictionary<string, int> PermitOverrides { get; init; } = new Dictionary<string, int>();
    public PlayIntegrityOptions PlayIntegrity { get; init; } = new();
    public AppAttestOptions AppAttest { get; init; } = new();

    /// <summary>
    /// Reads the section. With RequireAttestation on, outside Development and Test, both verifiers must be configured:
    /// the API refuses to start rather than silently letting unattested clients through.
    /// </summary>
    public static AbuseOptions Load(IConfiguration config, IHostEnvironment env)
    {
        var s = config.GetSection("Abuse");
        var play = s.GetSection("PlayIntegrity").Get<PlayIntegrityOptions>() ?? new();
        var apple = s.GetSection("AppAttest").Get<AppAttestOptions>() ?? new();
        var require = s.GetValue<bool>("RequireAttestation");
        var useFake = env.IsDevelopment() || env.IsEnvironment("Test");

        if (require && !useFake)
        {
            if (!play.IsConfigured) play.Validate();
            if (!apple.IsConfigured) apple.Validate();
        }
        if (!useFake)
        {
            if (play.IsConfigured) play.Validate();
            if (apple.IsConfigured) apple.Validate();
        }

        var overrides = s.GetSection("RateLimits").GetChildren()
            .Where(c => c.GetValue<int?>("Permit") is > 0)
            .ToDictionary(c => c.Key, c => c.GetValue<int>("Permit"));
        return new AbuseOptions
        {
            RequireAttestation = require,
            CloudflareRanges = s.GetSection("CloudflareRanges").Get<string[]>() is { Length: > 0 } r ? r : Abuse.CloudflareRanges.Default,
            DisposableEmailDomains = s.GetSection("DisposableEmailDomains").Get<string[]>() ?? [],
            // Test hosts share one loopback address and create many accounts; the limit itself is exercised with an explicit value.
            SignupsPerIpPerDay = s.GetValue<int?>("SignupsPerIpPerDay") ?? (env.IsEnvironment("Test") ? TestSignupsPerIpPerDay : DefaultSignupsPerIpPerDay),
            PermitOverrides = overrides,
            PlayIntegrity = play,
            AppAttest = apple,
        };
    }
}
