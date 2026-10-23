namespace Vela.Api.Images;

public sealed class StorageOptions
{
    public const string Section = "Storage";
    private const string DevConnectionString = "UseDevelopmentStorage=true";
    private const string DevPublicBaseUrl = "http://127.0.0.1:10000/devstoreaccount1/";

    public string ConnectionString { get; init; } = "";
    public string PublicBaseUrl { get; init; } = "";
    /// <summary>Physical name of the private container for raw uploads (never CDN-fronted).</summary>
    public string OriginalsContainer { get; init; } = BlobNames.Originals;

    /// <summary>Physical name of the private container for data-export archives (never CDN-fronted, never public).</summary>
    public string ExportsContainer { get; init; } = BlobNames.Exports;

    /// <summary>Physical name of the private container for illegal-content evidence (never CDN-fronted, never public).</summary>
    public string EvidenceContainer { get; init; } = BlobNames.Evidence;

    /// <summary>Outside Development and Test both settings are required; startup fails naming the missing one.</summary>
    public static StorageOptions Load(IConfiguration config, IHostEnvironment env)
    {
        var lenient = env.EnvironmentName is "Development" or "Test";
        var evidence = config[$"{Section}:Containers:Evidence"] is { Length: > 0 } e ? e : BlobNames.Evidence;
        var exports = config[$"{Section}:Containers:Exports"] is { Length: > 0 } x2 ? x2 : BlobNames.Exports;
        var originals = config[$"{Section}:Containers:Originals"] is { Length: > 0 } c2 ? c2 : BlobNames.Originals;
        if (evidence is BlobNames.Works or BlobNames.Avatars || evidence == exports || evidence == originals)
            throw new InvalidOperationException($"{Section}:Containers:Evidence must be a private container of its own, not a public, originals or exports container.");
        return new StorageOptions
        {
            ConnectionString = Require(config, "ConnectionString", lenient ? DevConnectionString : null),
            PublicBaseUrl = Require(config, "PublicBaseUrl", lenient ? DevPublicBaseUrl : null),
            OriginalsContainer = originals,
            ExportsContainer = exports,
            EvidenceContainer = evidence,
        };
    }

    private static string Require(IConfiguration config, string key, string? fallback) =>
        config[$"{Section}:{key}"] is { Length: > 0 } v ? v
        : fallback ?? throw new InvalidOperationException(
            $"{Section}:{key} is not configured. Set it via environment ({Section}__{key}) or a secret store.");
}
