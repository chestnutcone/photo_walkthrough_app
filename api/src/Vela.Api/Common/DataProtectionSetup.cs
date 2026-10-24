using Microsoft.AspNetCore.DataProtection;
using Azure.Storage.Blobs;

namespace Vela.Api.Common;

/// <summary>
/// ASP.NET Data Protection keys sign the admin cookie and antiforgery tokens. With several replicas, or a restart on a
/// scale-to-zero host, they must live outside the container: one private blob in the <c>dataprotection</c> container.
/// </summary>
public static class DataProtectionSetup
{
    public const string ApplicationName = "vela-api";
    public const string DefaultContainer = "dataprotection";
    public const string KeyBlob = "keys.xml";

    /// <summary>
    /// <c>DataProtection:PersistToBlob</c> defaults to on outside Development and Test. It reuses Storage:ConnectionString;
    /// the container (<c>DataProtection:Container</c>, default "dataprotection", private) is created by the infrastructure template.
    /// </summary>
    public static IServiceCollection AddVelaDataProtection(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var builder = services.AddDataProtection().SetApplicationName(ApplicationName);
        var persist = config.GetValue<bool?>("DataProtection:PersistToBlob") ?? !(env.IsDevelopment() || env.IsEnvironment("Test"));
        if (!persist) return services;

        var connection = config["Storage:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Storage:ConnectionString is required to persist Data Protection keys. Set it via environment (Storage__ConnectionString) or a secret store.");
        var container = config["DataProtection:Container"] is { Length: > 0 } c ? c : DefaultContainer;
        builder.PersistKeysToAzureBlobStorage(new BlobClient(connection, container, KeyBlob));
        return services;
    }
}
