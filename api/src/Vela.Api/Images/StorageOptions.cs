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

    /// <summary>Outside Development and Test both settings are required; startup fails naming the missing one.</summary>
    public static StorageOptions Load(IConfiguration config, IHostEnvironment env)
    {
        var lenient = env.EnvironmentName is "Development" or "Test";
        return new StorageOptions
        {
            ConnectionString = Require(config, "ConnectionString", lenient ? DevConnectionString : null),
            PublicBaseUrl = Require(config, "PublicBaseUrl", lenient ? DevPublicBaseUrl : null),
            OriginalsContainer = config[$"{Section}:Containers:Originals"] is { Length: > 0 } c ? c : BlobNames.Originals,
            ExportsContainer = config[$"{Section}:Containers:Exports"] is { Length: > 0 } x ? x : BlobNames.Exports,
        };
    }

    private static string Require(IConfiguration config, string key, string? fallback) =>
        config[$"{Section}:{key}"] is { Length: > 0 } v ? v
        : fallback ?? throw new InvalidOperationException(
            $"{Section}:{key} is not configured. Set it via environment ({Section}__{key}) or a secret store.");
}
