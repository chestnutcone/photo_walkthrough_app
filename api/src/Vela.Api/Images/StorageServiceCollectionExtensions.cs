namespace Vela.Api.Images;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddImageStorage(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var options = StorageOptions.Load(config, env);
        services.AddSingleton(options);
        services.AddSingleton(new AzureBlobStore(options));
        services.AddSingleton<IBlobStore>(sp => sp.GetRequiredService<AzureBlobStore>());
        if (env.EnvironmentName != "Test") services.AddHostedService<BlobContainerInitializer>();
        if (!(env.IsDevelopment() || env.IsEnvironment("Test")))
        {
            // Missing containers pass (they are created private); existing ones must have the expected access level.
            var dataProtection = config["DataProtection:Container"] is { Length: > 0 } c ? c : Common.DataProtectionSetup.DefaultContainer;
            services.AddHostedService(sp => new ContainerAccessGuard(
                new AzureContainerAccessReader(new Azure.Storage.Blobs.BlobServiceClient(options.ConnectionString)), options, dataProtection,
                sp.GetRequiredService<ILogger<ContainerAccessGuard>>()));
        }
        services.AddSingleton(_ => ImageDecodeGate.FromConfiguration(config));
        services.AddScoped<IVariantGenerator, VariantGenerator>();
        services.AddSingleton<DraftCleanupJob>();
        services.AddHostedService(sp => sp.GetRequiredService<DraftCleanupJob>());
        return services;
    }
}
