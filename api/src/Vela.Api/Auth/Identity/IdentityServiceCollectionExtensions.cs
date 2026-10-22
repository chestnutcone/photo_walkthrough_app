namespace Vela.Api.Auth.Identity;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityVerification(
        this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        services.Configure<IdentityOptions>(config.GetSection(IdentityOptions.Section));
        // Singleton: the JWKS cache must outlive requests.
        services.AddSingleton<IJwksProvider>(sp =>
            new HttpJwksProvider(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<IAccountResolver, AccountResolver>();
        services.AddSingleton<IIdentityVerifier, AppleIdentityVerifier>();
        services.AddSingleton<IIdentityVerifier, GoogleIdentityVerifier>();
        // Literal names on purpose: the fake provider must never exist in Production or Staging.
        if (env.EnvironmentName is "Development" or "Test")
            services.AddSingleton<IIdentityVerifier, FakeIdentityVerifier>();
        return services;
    }
}
