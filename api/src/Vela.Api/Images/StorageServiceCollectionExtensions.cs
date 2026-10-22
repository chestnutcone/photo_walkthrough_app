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
        services.AddScoped<IVariantGenerator, VariantGenerator>();
        services.AddSingleton<DraftCleanupJob>();
        services.AddHostedService(sp => sp.GetRequiredService<DraftCleanupJob>());
        return services;
    }
}
