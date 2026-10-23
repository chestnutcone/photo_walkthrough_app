using Vela.Api.Common;

namespace Vela.Api.Abuse;

public static class AbuseServiceExtensions
{
    /// <summary>Registers the Cloudflare-aware client IP resolver, rate limits, attestation, signup velocity and the disposable-email list.</summary>
    public static IServiceCollection AddAbuseProtection(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var options = AbuseOptions.Load(config, env);
        services.AddSingleton(options);
        services.AddSingleton<IClientIpResolver>(new ClientIpResolver(options.CloudflareRanges));
        services.AddSingleton(new DisposableEmailList(options.DisposableEmailDomains));
        services.AddScoped<SignupVelocity>();
        services.AddAbuseRateLimiting(options);
        AddAttestation(services, options, env);
        return services;
    }

    private static void AddAttestation(IServiceCollection services, AbuseOptions options, IHostEnvironment env)
    {
        if (env.IsDevelopment() || env.IsEnvironment("Test"))
        {
            services.AddSingleton<FakeAttestationVerifier>();
            services.AddSingleton<IAttestationVerifier>(sp => sp.GetRequiredService<FakeAttestationVerifier>());
            return;
        }
        // Options were validated when loaded; an unconfigured platform simply never verifies (and is only reachable when attestation is off).
        services.AddSingleton<IAttestationVerifier>(sp =>
        {
            var clock = sp.GetRequiredService<TimeProvider>();
            return new PlatformAttestationVerifier(
                options.PlayIntegrity.IsConfigured ? new PlayIntegrityVerifier(options.PlayIntegrity, clock) : null,
                options.AppAttest.IsConfigured ? new AppAttestVerifier(options.AppAttest, clock) : null);
        });
    }

    /// <summary>Call after UseAuthentication and before UseAuthorization.</summary>
    public static IApplicationBuilder UseAbuseProtection(this IApplicationBuilder app) =>
        app.UseRateLimiter()
            .UseMiddleware<RequestLimitsMiddleware>()
            .UseMiddleware<AttestationMiddleware>();
}
