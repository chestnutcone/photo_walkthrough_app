using Vela.Api.Auth;
using Vela.Api.Common;

namespace Vela.Api.Account;

public static class AccountEndpoints
{
    public static IServiceCollection AddAccount(this IServiceCollection services, IHostEnvironment env)
    {
        services.AddScoped<IAccountDeletionService, AccountDeletionService>().AddScoped<DeletionJob>()
            .AddSingleton<ICdnPurger, NoOpCdnPurger>().AddSingleton<IAppleTokenRevoker, LoggingAppleTokenRevoker>()
            .AddSingleton<DeletionWorker>();
        // Tests drive the sweeps by hand with a controllable clock; a real timer would race them.
        if (!env.IsEnvironment("Test"))
            services.AddHostedService(sp => sp.GetRequiredService<DeletionWorker>());
        return services;
    }

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/v1/me", async (ICurrentUser me, IAccountDeletionService svc) =>
        {
            await svc.RequestAsync(me.UserId!.Value);
            return Common.Results.Ok<object?>(null);
        }).RequireAuthorization().WithName("deleteAccount");

        return app;
    }
}
